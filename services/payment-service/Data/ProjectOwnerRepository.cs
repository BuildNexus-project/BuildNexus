using System.Data.Common;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET data access for <c>project_owners</c>. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class ProjectOwnerRepository : IProjectOwnerRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectOwnerRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> RecordOwnerIfAbsentAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // INSERT IGNORE: project_id is the primary key, so the one thing this
        // can silently skip is a project whose owner is already recorded —
        // exactly the idempotency a redelivered ProjectCreated needs. Note it
        // does not overwrite: the first value this service learned for a
        // project wins, which is right because the Project Service never
        // reassigns a project to a different Client.
        const string sql = @"
            INSERT IGNORE INTO project_owners (project_id, client_id, recorded_at)
            VALUES (@projectId, @clientId, @recordedAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);
        AddParameter(command, "@clientId", clientId);
        AddParameter(command, "@recordedAt", DateTime.UtcNow);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        return rowsAffected == 1;
    }

    public async Task<bool> IsOwnedByAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Both ids are bound, so the query answers the exact pair rather than
        // reading the row and comparing in C# — an unknown project and a
        // project owned by someone else come back the same way, and neither
        // leaks the real owner's id into this process.
        const string sql = @"
            SELECT EXISTS (
                SELECT 1
                FROM project_owners
                WHERE project_id = @projectId AND client_id = @clientId
            );";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);
        AddParameter(command, "@clientId", clientId);

        // MySQL's EXISTS yields 1 or 0 as a signed integer; Convert handles the
        // width MySqlConnector picks without pinning this to one CLR type.
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is not null && Convert.ToInt64(result) == 1;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
