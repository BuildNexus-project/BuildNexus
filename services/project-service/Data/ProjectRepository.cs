using System.Data.Common;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// ADO.NET data access for the <c>projects</c> table and its status history.
/// Direct SQL only — no ORM. Every value reaches MySQL as a bound parameter.
/// </summary>
public class ProjectRepository : IProjectRepository
{
    private const string SelectColumns =
        "id, client_id, name, location, land_size_perches, budget, floors, bedrooms, bathrooms, "
        + "garage_spaces, other_requirements, status, assigned_architect_id, "
        + "assigned_project_manager_id, created_at, updated_at";

    private const string SelectHistoryColumns =
        "id, project_id, from_status, to_status, changed_by_user_id, changed_by_role, changed_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(Project project, ProjectStatusChange creation)
    {
        // The assignment columns are absent on purpose: a new project has nobody
        // on it, and nothing in the system assigns staff yet.
        const string sql = @"
            INSERT INTO projects
                (id, client_id, name, location, land_size_perches, budget, floors, bedrooms,
                 bathrooms, garage_spaces, other_requirements, status, created_at, updated_at)
            VALUES
                (@id, @clientId, @name, @location, @landSizePerches, @budget, @floors, @bedrooms,
                 @bathrooms, @garageSpaces, @otherRequirements, @status, @createdAt, @updatedAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        // Both statements or neither: a project whose history does not start at
        // its creation has an audit trail that begins halfway through, and there
        // is no moment at which that is an acceptable state to be in.
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            AddParameter(command, "@id", project.Id);
            AddParameter(command, "@clientId", project.ClientId);
            AddParameter(command, "@name", project.Name);
            AddParameter(command, "@location", project.Location);
            AddParameter(command, "@landSizePerches", project.LandSizePerches);
            AddParameter(command, "@budget", project.Budget);
            AddParameter(command, "@floors", project.Floors);
            AddParameter(command, "@bedrooms", project.Bedrooms);
            AddParameter(command, "@bathrooms", project.Bathrooms);
            AddParameter(command, "@garageSpaces", project.GarageSpaces);
            AddParameter(command, "@otherRequirements", project.OtherRequirements);
            // Stored as the enum's name, which is what ck_projects_status checks
            // against — never its underlying number.
            AddParameter(command, "@status", project.Status.ToString());
            AddParameter(command, "@createdAt", project.CreatedAt);
            AddParameter(command, "@updatedAt", project.UpdatedAt);

            await command.ExecuteNonQueryAsync();
        }

        await InsertStatusChangeAsync(connection, transaction, creation);

        await transaction.CommitAsync();
    }

    public async Task<Project?> GetByIdAsync(Guid id)
    {
        const string sql = $"SELECT {SelectColumns} FROM projects WHERE id = @id LIMIT 1;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProject(reader) : null;
    }

    public async Task<IReadOnlyList<Project>> ListAllAsync()
    {
        // Newest first: the project somebody is looking for is far more often
        // the one that just came in than the one from last year.
        const string sql = $"SELECT {SelectColumns} FROM projects ORDER BY created_at DESC;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await ReadProjectListAsync(command);
    }

    public async Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId)
    {
        // The same three ways of being involved in a project that decide whether
        // the caller may open one, applied as a filter here rather than to each
        // row in turn: submitted it, or is the Architect or Project Manager on
        // it.
        const string sql = $@"
            SELECT {SelectColumns}
            FROM projects
            WHERE client_id = @userId
               OR assigned_architect_id = @userId
               OR assigned_project_manager_id = @userId
            ORDER BY created_at DESC;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@userId", userId);

        return await ReadProjectListAsync(command);
    }

    public async Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId)
    {
        // Oldest first, which is the order the story asks the view to show them
        // in. `id` breaks a tie so two changes stamped in the same second still
        // come back in a stable order rather than whatever the engine feels
        // like.
        const string sql = $@"
            SELECT {SelectHistoryColumns}
            FROM project_status_history
            WHERE project_id = @projectId
            ORDER BY changed_at, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var history = new List<ProjectStatusChange>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            history.Add(MapStatusChange(reader));
        }

        return history;
    }

    public async Task<bool> UpdateStatusAsync(ProjectStatusChange change, DateTime updatedAtUtc)
    {
        // `status = @fromStatus` in the WHERE clause is the concurrency guard:
        // if somebody else moved the project between the read that decided this
        // transition was valid and this write, no row matches and nothing is
        // written — rather than two transitions landing on top of each other.
        const string sql = @"
            UPDATE projects
            SET status     = @toStatus,
                updated_at = @updatedAt
            WHERE id = @id
              AND status = @fromStatus;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        // The move and its record are one write or neither. A status that
        // changed without a history row is exactly the untraceable change the
        // table exists to prevent.
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            AddParameter(command, "@toStatus", change.ToStatus.ToString());
            AddParameter(command, "@updatedAt", updatedAtUtc);
            AddParameter(command, "@id", change.ProjectId);
            // Never null on a transition — only the opening entry has no
            // previous status, and that one is written by InsertAsync.
            AddParameter(command, "@fromStatus", change.FromStatus?.ToString());

            if (await command.ExecuteNonQueryAsync() == 0)
            {
                await transaction.RollbackAsync();
                return false;
            }
        }

        await InsertStatusChangeAsync(connection, transaction, change);

        await transaction.CommitAsync();

        return true;
    }

    /// <summary>
    /// Appends one history row on an existing connection and transaction, so it
    /// lands with the project write it belongs to.
    /// </summary>
    private static async Task InsertStatusChangeAsync(
        DbConnection connection,
        DbTransaction transaction,
        ProjectStatusChange change)
    {
        const string sql = @"
            INSERT INTO project_status_history
                (id, project_id, from_status, to_status, changed_by_user_id, changed_by_role, changed_at)
            VALUES
                (@id, @projectId, @fromStatus, @toStatus, @changedByUserId, @changedByRole, @changedAt);";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@id", change.Id);
        AddParameter(command, "@projectId", change.ProjectId);
        // NULL on the opening entry: the project did not come from anywhere.
        AddParameter(command, "@fromStatus", change.FromStatus?.ToString());
        AddParameter(command, "@toStatus", change.ToStatus.ToString());
        AddParameter(command, "@changedByUserId", change.ChangedByUserId);
        AddParameter(command, "@changedByRole", change.ChangedByRole);
        AddParameter(command, "@changedAt", change.ChangedAt);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<Project>> ReadProjectListAsync(DbCommand command)
    {
        var projects = new List<Project>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            projects.Add(MapProject(reader));
        }

        return projects;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // Omitted free-text requirements are a real NULL in the column, not an
        // empty string, and so is an unassigned architect or project manager.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    /// <summary>
    /// A status column back into the enum. The name is what is stored, so this
    /// is the exact inverse of what the inserts write.
    /// </summary>
    private static ProjectStatus? ParseNullableStatus(DbDataReader reader, string column)
    {
        var value = GetNullableString(reader, column);

        return value is null ? null : Enum.Parse<ProjectStatus>(value);
    }

    private static Project MapProject(DbDataReader reader) => new()
    {
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        ClientId = reader.GetGuid(reader.GetOrdinal("client_id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Location = reader.GetString(reader.GetOrdinal("location")),
        LandSizePerches = reader.GetDecimal(reader.GetOrdinal("land_size_perches")),
        Budget = reader.GetDecimal(reader.GetOrdinal("budget")),
        Floors = reader.GetInt32(reader.GetOrdinal("floors")),
        Bedrooms = reader.GetInt32(reader.GetOrdinal("bedrooms")),
        Bathrooms = reader.GetInt32(reader.GetOrdinal("bathrooms")),
        GarageSpaces = reader.GetInt32(reader.GetOrdinal("garage_spaces")),
        OtherRequirements = GetNullableString(reader, "other_requirements"),
        Status = Enum.Parse<ProjectStatus>(reader.GetString(reader.GetOrdinal("status"))),
        AssignedArchitectId = GetNullableGuid(reader, "assigned_architect_id"),
        AssignedProjectManagerId = GetNullableGuid(reader, "assigned_project_manager_id"),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };

    private static ProjectStatusChange MapStatusChange(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        FromStatus = ParseNullableStatus(reader, "from_status"),
        ToStatus = Enum.Parse<ProjectStatus>(reader.GetString(reader.GetOrdinal("to_status"))),
        ChangedByUserId = reader.GetGuid(reader.GetOrdinal("changed_by_user_id")),
        ChangedByRole = reader.GetString(reader.GetOrdinal("changed_by_role")),
        ChangedAt = reader.GetDateTime(reader.GetOrdinal("changed_at"))
    };
}
