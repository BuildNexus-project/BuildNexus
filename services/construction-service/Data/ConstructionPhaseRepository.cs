using System.Data.Common;
using BuildNexus.ConstructionService.Messaging;
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
        "project_id, status, started_at, completed_at, handed_over_at, handed_over_by, updated_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public ConstructionPhaseRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ConstructionTransitionResult> StartAsync(
        Guid projectId,
        Guid startedBy,
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
        var milestoneCount = await CountMilestonesAsync(connection, transaction, projectId, cancellationToken);

        if (milestoneCount == 0)
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
            HandedOverByUserId = null,
            UpdatedAtUtc = now
        };

        const string insertSql = @"
            INSERT INTO construction_phases
                (project_id, status, started_at, completed_at, handed_over_at, handed_over_by, updated_at)
            VALUES
                (@projectId, @status, @startedAt, NULL, NULL, NULL, @updatedAt);";

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

        // AC-1's event, enqueued in the same transaction as the phase row it
        // announces: the two commit together or not at all, so there is no outcome
        // in which the build is started and nobody is ever told. Getting it onto
        // construction-events is OutboxDispatcher's job from here.
        await OutboxRepository.InsertAsync(
            connection,
            transaction,
            ConstructionEvents.Started(phase, milestoneCount, startedBy),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ConstructionTransitionResult.Succeeded(phase);
    }

    public async Task<ConstructionTransitionResult> CompleteAsync(
        Guid projectId,
        Guid completedBy,
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

        // Built from the row this transaction already read plus the two values it
        // just wrote, rather than a second SELECT: the UPDATE ran under the row
        // lock, so nothing else can have changed the rest of the row in between.
        var completedPhase = new ConstructionPhase
        {
            ProjectId = existing.ProjectId,
            Status = ConstructionPhaseStatus.Completed,
            StartedAtUtc = existing.StartedAtUtc,
            CompletedAtUtc = now,
            HandedOverAtUtc = existing.HandedOverAtUtc,
            HandedOverByUserId = existing.HandedOverByUserId,
            UpdatedAtUtc = now
        };

        // AC-2's event, enqueued in the same transaction as the UPDATE above. The
        // milestone tally that satisfied the gate rides along on the payload rather
        // than being counted again at dispatch, where it could have moved.
        await OutboxRepository.InsertAsync(
            connection,
            transaction,
            ConstructionEvents.Completed(completedPhase, total, completedBy),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ConstructionTransitionResult.Succeeded(completedPhase);
    }

    public async Task<ConstructionTransitionResult> HandOverAsync(
        Guid projectId,
        Guid handedOverBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Locked for the length of the transaction, so two handovers cannot both read
        // the same Completed row and both act on it.
        var existing = await ReadPhaseForUpdateAsync(connection, transaction, projectId, cancellationToken);

        if (existing is null)
        {
            // The build was never started, so there is nothing to hand over.
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.NotStarted);
        }

        if (existing.Status is ConstructionPhaseStatus.HandedOver)
        {
            // Terminal: nothing may leave this state, a second handover included.
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.AlreadyHandedOver);
        }

        if (existing.Status is not ConstructionPhaseStatus.Completed)
        {
            // Still Started. AC-4 hands over a finished project, and this is its own
            // answer rather than being folded into "not started" — the build is under
            // way, which is a different thing for the PM to be told.
            return ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.NotCompleted);
        }

        // AC-4's second precondition, checked independently of the first: the final
        // payment must be settled. Read from the local marker the FinalPaymentSettled
        // consumer plants, in this same transaction — the Payment Service is never
        // called on this path.
        if (!await IsFinalPaymentSettledAsync(connection, transaction, projectId, cancellationToken))
        {
            return ConstructionTransitionResult.Rejected(
                ConstructionTransitionOutcome.FinalPaymentNotSettled);
        }

        var now = DateTime.UtcNow;

        // The WHERE carries the status this transaction read under the row lock, so a
        // concurrent transition cannot slip a different state in underneath.
        const string updateSql = @"
            UPDATE construction_phases
            SET status = @status,
                handed_over_at = @handedOverAt,
                handed_over_by = @handedOverBy,
                updated_at = @updatedAt
            WHERE project_id = @projectId AND status = @expectedStatus;";

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = updateSql;
            AddParameter(command, "@status", ConstructionPhaseStatus.HandedOver.ToString());
            AddParameter(command, "@handedOverAt", now);
            AddParameter(command, "@handedOverBy", handedOverBy);
            AddParameter(command, "@updatedAt", now);
            AddParameter(command, "@projectId", projectId);
            AddParameter(command, "@expectedStatus", ConstructionPhaseStatus.Completed.ToString());

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ConstructionTransitionResult.Rejected(
                    ConstructionTransitionOutcome.AlreadyHandedOver);
            }
        }

        // Nothing is enqueued: handover raises no event. US-14 names two, and the
        // Project Service already reaches Completed on ConstructionCompleted.
        await transaction.CommitAsync(cancellationToken);

        return ConstructionTransitionResult.Succeeded(new ConstructionPhase
        {
            ProjectId = existing.ProjectId,
            Status = ConstructionPhaseStatus.HandedOver,
            StartedAtUtc = existing.StartedAtUtc,
            CompletedAtUtc = existing.CompletedAtUtc,
            HandedOverAtUtc = now,
            HandedOverByUserId = handedOverBy,
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

    /// <summary>
    /// Is this project's final payment recorded as settled — the local answer to AC-4's
    /// payment precondition, planted by <c>PaymentEventsConsumer</c>.
    /// </summary>
    /// <remarks>
    /// Read here rather than through <see cref="IPaymentSettlementRepository"/> so it
    /// runs on the caller's connection and inside its transaction: a settlement read on
    /// a second connection could be revoked between the check and the write, and AC-4's
    /// whole point is that an unpaid project is not handed over.
    /// <para>
    /// A project with no row reads as not settled, which covers both "not paid" and
    /// "this service has not learned of the payment yet".
    /// </para>
    /// </remarks>
    private static async Task<bool> IsFinalPaymentSettledAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT 1
            FROM payment_settlements
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
        HandedOverByUserId = ReadNullableGuid(reader, "handed_over_by"),
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
    /// <summary>
    /// Reads a nullable <c>CHAR(36)</c> id. Null on a phase that has not reached the
    /// stage the column records.
    /// </summary>
    private static Guid? ReadNullableGuid(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTime? ReadUtc(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }
}
