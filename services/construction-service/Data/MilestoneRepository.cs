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
        CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}