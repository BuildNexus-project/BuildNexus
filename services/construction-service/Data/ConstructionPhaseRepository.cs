using System.Data.Common;
using BuildNexus.ConstructionService.Models;
using MySqlConnector;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// ADO.NET data access for <c>construction_phases</c>, and the three gated
/// transitions US-14 defines over it. Direct SQL only — no ORM. Every value
/// reaches MySQL as a bound parameter.
/// </summary>
/// <remarks>
/// The gates live here rather than in the controller because every one of them is
/// a question about rows, and asking them anywhere else would mean reading state
/// in one connection and writing it in another — the window AC-3's rejections
/// exist to close. Each transition opens one transaction, checks only its own
/// preconditions inside it, and writes in the same transaction.
/// </remarks>
public class ConstructionPhaseRepository : IConstructionPhaseRepository
{
    /// <summary>MySQL's "duplicate entry for key" error number — the signal a UNIQUE key rejected the row.</summary>
    private const int DuplicateKeyErrorNumber = 1062;

    private const string SelectColumns =
        "project_id, status, started_at, completed_at, handed_over_at, updated_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public ConstructionPhaseRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ConstructionTransitionResult> StartAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // AC-1's first precondition: the design is approved. Read from the marker
        // US-23's consumer plants, exactly as MilestoneRepository reads it — the
        // fact is not re-derived or copied here.
        if (!await IsDesignApprovedAsync(connection, transaction, projectId, cancellationToken))
        {
            // Nothing written, so nothing to roll back; the transaction closes
            // cleanly when it is disposed.
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.DesignNotApproved);
        }

        // AC-1's second precondition, checked on its own: "milestones defined".
        // A project can have an approved design and no plan at all, and starting
        // a build with nothing to track is the case AC-3 asks to be refused.
        if (await CountMilestonesAsync(connection, transaction, projectId, cancellationToken) == 0)
        {
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.NoMilestonesDefined);
        }

        var now = DateTime.UtcNow;
        var phase = new ConstructionPhase
        {
            ProjectId = projectId,
            Status = ConstructionPhaseStatus.Started,
            StartedAtUtc = now,
            CompletedAtUtc = null,
            HandedOverAtUtc = null,
            UpdatedAtUtc = now
        };

        const string insertSql = @"
            INSERT INTO construction_phases
                (project_id, status, started_at, completed_at, handed_over_at, updated_at)
            VALUES
                (@projectId, @status, @startedAt, NULL, NULL, @updatedAt);";

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = insertSql;
                AddParameter(command, "@projectId", phase.ProjectId);
                AddParameter(command, "@status", phase.Status.ToString());
                AddParameter(command, "@startedAt", phase.StartedAtUtc);
                AddParameter(command, "@updatedAt", phase.UpdatedAtUtc);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (MySqlException ex) when (ex.Number == DuplicateKeyErrorNumber)
        {
            // pk_construction_phases refused a second row: the build is already
            // started (or past it). Letting the primary key answer this rather
            // than a SELECT beforehand is what makes two simultaneous starts
            // safe — one commits, the other lands here.
            await transaction.RollbackAsync(cancellationToken);
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.AlreadyStarted);
        }

        await transaction.CommitAsync(cancellationToken);
        return ConstructionTransitionResult.Succeeded(phase);
    }

    public async Task<ConstructionTransitionResult> CompleteAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Locked for the length of the transaction, so a concurrent complete or
        // handover cannot read the same 'Started' row and both act on it.
        var existing = await ReadPhaseForUpdateAsync(connection, transaction, projectId, cancellationToken);

        if (existing is null)
        {
            // AC-3's named case: construction was never started, so there is
            // nothing to complete.
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.NotStarted);
        }

        if (existing.Status is not ConstructionPhaseStatus.Started)
        {
            // Already Completed, or already HandedOver. Either way this
            // transition has nothing left to do.
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.AlreadyCompleted);
        }

        // AC-2: every milestone Completed. Counted in one query rather than read
        // and tallied in C# — and a project with no milestones fails it too, so
        // "0 of 0 are done" cannot be mistaken for a finished build.
        var (total, completed) = await CountMilestonesByStatusAsync(
            connection, transaction, projectId, cancellationToken);

        if (total == 0 || completed != total)
        {
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.MilestonesIncomplete);
        }

        var now = DateTime.UtcNow;

        // The WHERE still carries the status this transaction read under the
        // row lock — belt and braces, and it keeps the statement correct if the
        // locking above is ever loosened.
        const string updateSql = @"
            UPDATE construction_phases
            SET status = @status, completed_at = @completedAt, updated_at = @updatedAt
            WHERE project_id = @projectId AND status = @expectedStatus;";

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = updateSql;
            AddParameter(command, "@status", ConstructionPhaseStatus.Completed.ToString());
            AddParameter(command, "@completedAt", now);
            AddParameter(command, "@updatedAt", now);
            AddParameter(command, "@projectId", projectId);
            AddParameter(command, "@expectedStatus", ConstructionPhaseStatus.Started.ToString());

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.AlreadyCompleted);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Built from the row this transaction already read plus the two values it
        // just wrote, rather than a second SELECT: the UPDATE ran under the row
        // lock, so nothing else can have changed the rest of the row in between.
        return ConstructionTransitionResult.Succeeded(new ConstructionPhase
        {
            ProjectId = existing.ProjectId,
            Status = ConstructionPhaseStatus.Completed,
            StartedAtUtc = existing.StartedAtUtc,
            CompletedAtUtc = now,
            HandedOverAtUtc = existing.HandedOverAtUtc,
            UpdatedAtUtc = now
        });
    }

    public async Task<ConstructionPhase?> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM construction_phases
            WHERE project_id = @projectId;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    /// <summary>
    /// Reads the phase row and holds it for the rest of the transaction, so a
    /// concurrent transition cannot act on the same state.
    /// </summary>
    private static async Task<ConstructionPhase?> ReadPhaseForUpdateAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM construction_phases
            WHERE project_id = @projectId
            FOR UPDATE;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    /// <summary>
    /// Does this project have a <c>milestone_setups</c> row — the local answer to
    /// "has the design been approved?", planted by the <c>DesignApproved</c>
    /// consumer (US-23). The same question
    /// <see cref="MilestoneRepository"/> asks before creating a milestone.
    /// </summary>
    private static async Task<bool> IsDesignApprovedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT 1
            FROM milestone_setups
            WHERE project_id = @projectId
            LIMIT 1;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>How many milestones the project has, in any state.</summary>
    private static async Task<int> CountMilestonesAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM construction_milestones
            WHERE project_id = @projectId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// The project's milestone tally — how many there are, and how many are
    /// <c>Completed</c> — in one round trip.
    /// </summary>
    private static async Task<(int Total, int Completed)> CountMilestonesByStatusAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                COUNT(*)                                                           AS total,
                COALESCE(SUM(CASE WHEN status = 'Completed' THEN 1 ELSE 0 END), 0) AS completed
            FROM construction_milestones
            WHERE project_id = @projectId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // An aggregate query always returns one row.
        await reader.ReadAsync(cancellationToken);

        return (
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("total"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("completed"))));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static ConstructionPhase Map(DbDataReader reader) => new()
    {
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        Status = Enum.Parse<ConstructionPhaseStatus>(reader.GetString(reader.GetOrdinal("status"))),
        StartedAtUtc = ReadUtc(reader, "started_at")!.Value,
        CompletedAtUtc = ReadUtc(reader, "completed_at"),
        HandedOverAtUtc = ReadUtc(reader, "handed_over_at"),
        UpdatedAtUtc = ReadUtc(reader, "updated_at")!.Value
    };

    /// <summary>
    /// Reads a timestamp column back as UTC.
    /// </summary>
    /// <remarks>
    /// MySQL's <c>DATETIME</c> carries no timezone, so MySqlConnector hands the
    /// value back as <see cref="DateTimeKind.Unspecified"/> — which
    /// System.Text.Json then serialises without a <c>Z</c>, and the browser reads
    /// as local time. Every write path here uses <c>DateTime.UtcNow</c>, so
    /// stamping the Kind on the way out restores the invariant the write side
    /// already keeps. Same reasoning as <see cref="MilestoneRepository"/>'s own
    /// mapping.
    /// </remarks>
    private static DateTime? ReadUtc(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }
}
