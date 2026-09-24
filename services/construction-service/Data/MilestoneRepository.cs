using System.Data;
using System.Data.Common;
using BuildNexus.ConstructionService.Models;
using MySqlConnector;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// ADO.NET data access for <c>construction_milestones</c>. Direct SQL only —
/// no ORM. Every value reaches MySQL as a bound parameter.
/// </summary>
public class MilestoneRepository : IMilestoneRepository
{
    /// <summary>MySQL's "duplicate entry for key" error number — the signal a UNIQUE key rejected the row.</summary>
    private const int DuplicateKeyErrorNumber = 1062;

    private const string SelectColumns =
        "id, project_id, name, status, created_at, updated_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public MilestoneRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Milestone?> CreateAsync(
        Guid projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();

        // Gate check and insert on one transaction: the milestone_setups row
        // must be there when the INSERT lands, and a caller cannot see the
        // half-state of "gate passed but not yet stored". Serializable is
        // overkill for two statements against a small table — the default
        // REPEATABLE READ is enough to keep the gate honest here.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await IsDesignApprovedAsync(connection, transaction, projectId, cancellationToken))
            {
                // No rollback needed — nothing was written. Returning null lets
                // the controller map this to 400 with a specific reason.
                return null;
            }

            var now = DateTime.UtcNow;
            var milestone = new Milestone
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = name,
                Status = MilestoneStatus.NotStarted,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            const string insertSql = $@"
                INSERT INTO construction_milestones ({SelectColumns})
                VALUES (@id, @projectId, @name, @status, @createdAt, @updatedAt);";

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = insertSql;
                AddParameter(command, "@id", milestone.Id);
                AddParameter(command, "@projectId", milestone.ProjectId);
                AddParameter(command, "@name", milestone.Name);
                AddParameter(command, "@status", milestone.Status.ToString());
                AddParameter(command, "@createdAt", milestone.CreatedAtUtc);
                AddParameter(command, "@updatedAt", milestone.UpdatedAtUtc);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return milestone;
        }
        catch (MySqlException ex) when (ex.Number == DuplicateKeyErrorNumber)
        {
            // uq_construction_milestones_project_name refused a repeat name.
            // Rollback first so the transaction is not left hanging on the
            // connection when the exception unwinds.
            await transaction.RollbackAsync(cancellationToken);
            throw new DuplicateMilestoneNameException(projectId, name);
        }
    }

    public async Task<Milestone?> UpdateStatusAsync(
        Guid milestoneId,
        MilestoneStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        // Two statements on one connection: the UPDATE moves the row, and the
        // SELECT that follows reads it back so the caller (and the AC-3
        // progress recalculation the caller does next) sees the same
        // updated_at. Wrapped in a transaction so a concurrent PATCH on the
        // same row cannot slip between them.
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string updateSql = @"
            UPDATE construction_milestones
            SET status = @status, updated_at = @updatedAt
            WHERE id = @id;";

        int rowsAffected;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = updateSql;
            AddParameter(command, "@status", newStatus.ToString());
            AddParameter(command, "@updatedAt", DateTime.UtcNow);
            AddParameter(command, "@id", milestoneId);

            rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (rowsAffected == 0)
        {
            // No such milestone — the controller maps this to 404. Nothing to
            // roll back, but the transaction still needs to close cleanly.
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        const string selectSql = $@"
            SELECT {SelectColumns}
            FROM construction_milestones
            WHERE id = @id;";

        Milestone? milestone;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = selectSql;
            AddParameter(command, "@id", milestoneId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            milestone = await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
        }

        await transaction.CommitAsync(cancellationToken);
        return milestone;
    }

    public async Task<IReadOnlyList<Milestone>?> CreateFromTemplateAsync(
        Guid projectId,
        IReadOnlyList<string> templateNames,
        CancellationToken cancellationToken = default)
    {
        // Empty template is a no-op — return an empty list so the caller
        // still gets the "not null" signal that means "the gate passed",
        // without a pointless transaction round trip.
        if (templateNames.Count == 0)
        {
            // Still gate-check before returning empty, so a caller cannot use
            // an empty template to probe whether a project's design has been
            // approved. Same shape as the non-empty path returns.
            await using var probeConnection = await _connectionFactory.OpenConnectionAsync();
            return await IsDesignApprovedAsync(probeConnection, transaction: null, projectId, cancellationToken)
                ? Array.Empty<Milestone>()
                : null;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await IsDesignApprovedAsync(connection, transaction, projectId, cancellationToken))
        {
            // Gate refused — return null so the controller maps to 400.
            // No rollback needed; nothing was written.
            return null;
        }

        // INSERT ... SELECT ... WHERE NOT EXISTS makes each insert idempotent
        // in one round trip — MySQL checks for the (project_id, name) pair
        // and inserts only if it is not already there. No SELECT-then-INSERT
        // race window inside this transaction: the WHERE NOT EXISTS is
        // evaluated by the engine at insert time, holding whatever locks the
        // engine needs. Names already on the project are silently skipped.
        //
        // A duplicate-name insert from a concurrent transaction would still
        // hit the UNIQUE key and throw error 1062; that is genuinely
        // exceptional here (two PMs applying the template at the same
        // millisecond) and rolling back the whole batch is the safe answer.
        const string insertSql = @"
            INSERT INTO construction_milestones (id, project_id, name, status, created_at, updated_at)
            SELECT @id, @projectId, @name, @status, @createdAt, @updatedAt
            WHERE NOT EXISTS (
                SELECT 1 FROM construction_milestones
                WHERE project_id = @projectId AND name = @name
            );";

        // The status stamp is the same for every insert in this batch: they
        // are all planted at once, at the same moment, and share creation
        // time. The per-row created_at gets a one-microsecond bump per row
        // so the list ordering matches the canonical template order — see
        // MilestoneTemplates for why that order matters.
        var stampBase = DateTime.UtcNow;

        for (var index = 0; index < templateNames.Count; index++)
        {
            var name = templateNames[index];
            // One microsecond per row, not one Tick. A Tick is 100 ns — a tenth
            // of what DATETIME(6) can store — so a per-Tick bump is truncated
            // away on write and all seven rows land on the same created_at.
            // ORDER BY created_at, id would then fall through to the random
            // Guid tiebreaker and hand back the template in arbitrary order.
            var stamp = stampBase.AddTicks(index * TimeSpan.TicksPerMicrosecond);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = insertSql;
            AddParameter(command, "@id", Guid.NewGuid());
            AddParameter(command, "@projectId", projectId);
            AddParameter(command, "@name", name);
            AddParameter(command, "@status", MilestoneStatus.NotStarted.ToString());
            AddParameter(command, "@createdAt", stamp);
            AddParameter(command, "@updatedAt", stamp);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // Return the current state of the template names on this project —
        // union of what was already there and what this call just inserted.
        // The caller ignores what it already had and appends the new rows,
        // so returning the whole set is the least confusing shape.
        return await ListForProjectFilteredByNamesAsync(projectId, templateNames, cancellationToken);
    }

    /// <summary>
    /// Reads the milestones a project has whose name is in a given set.
    /// Used by <see cref="CreateFromTemplateAsync"/> to report the post
    /// -template state without pulling every unrelated milestone the PM
    /// may have added.
    /// </summary>
    private async Task<IReadOnlyList<Milestone>> ListForProjectFilteredByNamesAsync(
        Guid projectId,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        // IN (@n0, @n1, ...) with an explicit parameter per name — never a
        // string-concatenated list. Names are user-facing template constants
        // today but the same code path would work for any caller-supplied
        // list, and a parameterised IN clause is the shape that survives
        // that change safely.
        var parameterNames = names.Select((_, index) => $"@n{index}").ToArray();

        var sql = $@"
            SELECT {SelectColumns}
            FROM construction_milestones
            WHERE project_id = @projectId AND name IN ({string.Join(", ", parameterNames)})
            ORDER BY created_at, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);
        for (var index = 0; index < names.Count; index++)
        {
            AddParameter(command, parameterNames[index], names[index]);
        }

        var rows = new List<Milestone>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }
        return rows;
    }

    public async Task<IReadOnlyList<Milestone>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM construction_milestones
            WHERE project_id = @projectId
            ORDER BY created_at, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var rows = new List<Milestone>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    public async Task<ProjectProgress?> GetProgressForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();

        // The gate again: a project whose design has not been approved has no
        // "progress" to report — a percentage there would falsely say 0% of a
        // plan that does not exist. The controller maps null to 404.
        if (!await IsDesignApprovedAsync(connection, transaction: null, projectId, cancellationToken))
        {
            return null;
        }

        // One round trip: COUNT and the completed subtotal in the same query,
        // computed by MySQL from the current rows — no stored percentage to
        // drift (AC-3). SUM over a boolean CASE returns NULL when the project
        // has no milestones; COALESCE keeps it a plain int.
        const string sql = @"
            SELECT
                COUNT(*)                                                   AS total,
                COALESCE(SUM(CASE WHEN status = 'Completed' THEN 1 ELSE 0 END), 0) AS completed
            FROM construction_milestones
            WHERE project_id = @projectId;";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Aggregate queries always return one row.
        await reader.ReadAsync(cancellationToken);

        var total = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("total")));
        var completed = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("completed")));

        return new ProjectProgress
        {
            ProjectId = projectId,
            TotalMilestones = total,
            CompletedMilestones = completed,
            ProgressPercent = CalculatePercent(completed, total)
        };
    }

    /// <summary>
    /// AC-3's percentage, rounded to two decimals. Zero when the project has
    /// no milestones — the alternative, dividing by zero, is a real bug the
    /// callers should not have to guard against.
    /// </summary>
    private static decimal CalculatePercent(int completed, int total) =>
        total == 0 ? 0m : Math.Round((decimal)completed / total * 100m, 2);

    /// <summary>
    /// Does this project have a <c>milestone_setups</c> row — the local
    /// answer to "has the design been approved?", planted by the
    /// <c>DesignApproved</c> Kafka consumer (US-23).
    /// </summary>
    private static async Task<bool> IsDesignApprovedAsync(
        DbConnection connection,
        DbTransaction? transaction,
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

        var found = await command.ExecuteScalarAsync(cancellationToken);
        return found is not null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Milestone Map(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        // Persisted as its own name for legibility, so the round trip is a
        // plain enum parse — no int-to-name mapping to keep in sync.
        Status = Enum.Parse<MilestoneStatus>(reader.GetString(reader.GetOrdinal("status"))),
        // MySQL's DATETIME type has no timezone attached, so MySqlConnector
        // reads the value back as DateTimeKind.Unspecified — it cannot know
        // the column stores UTC. System.Text.Json then serializes that
        // Unspecified value without a Z suffix, and the browser reads the
        // no-suffix ISO string as local time — so "Last updated" would show
        // 5h 30m off after a reload for a Colombo caller (and 8h off for a
        // Singapore caller, etc). The columns do store UTC — every write
        // path here uses DateTime.UtcNow — so stamping the read Kind as Utc
        // restores the invariant the write side already keeps.
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("created_at")), DateTimeKind.Utc),
        UpdatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("updated_at")), DateTimeKind.Utc)
    };
}