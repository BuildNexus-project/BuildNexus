using System.Data.Common;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// ADO.NET data access for <c>milestone_setups</c>. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class MilestoneSetupRepository : IMilestoneSetupRepository
{
    private const string SelectColumns =
        "id, project_id, source_document_id, source_event_id, approved_at, created_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public MilestoneSetupRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> CreatePlaceholderIfAbsentAsync(
        Guid projectId,
        Guid sourceDocumentId,
        Guid sourceEventId,
        DateTime approvedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // INSERT IGNORE: the only unique index is on project_id, and id is a
        // fresh Guid, so the one thing this can silently skip is a project that
        // already has a placeholder — which is exactly the idempotency US-23
        // asks for. A second approved document, or a redelivered event, hits it
        // and affects zero rows.
        const string sql = $@"
            INSERT IGNORE INTO milestone_setups ({SelectColumns})
            VALUES (@id, @projectId, @sourceDocumentId, @sourceEventId, @approvedAt, @createdAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", Guid.NewGuid());
        AddParameter(command, "@projectId", projectId);
        AddParameter(command, "@sourceDocumentId", sourceDocumentId);
        AddParameter(command, "@sourceEventId", sourceEventId);
        AddParameter(command, "@approvedAt", approvedAtUtc);
        AddParameter(command, "@createdAt", DateTime.UtcNow);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        return rowsAffected == 1;
    }

    public async Task<IReadOnlyList<MilestoneSetup>> ListAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM milestone_setups
            ORDER BY created_at DESC, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<MilestoneSetup>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static MilestoneSetup Map(DbDataReader reader) => new()
    {
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        SourceDocumentId = reader.GetGuid(reader.GetOrdinal("source_document_id")),
        SourceEventId = reader.GetGuid(reader.GetOrdinal("source_event_id")),
        ApprovedAtUtc = reader.GetDateTime(reader.GetOrdinal("approved_at")),
        CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
