using System.Data.Common;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET data access for <c>quotations</c>. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class QuotationRepository : IQuotationRepository
{
    private const string SelectColumns =
        "id, project_id, estimated_total, created_by, created_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public QuotationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Quotation> CreateAsync(
        Guid projectId,
        decimal estimatedTotal,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EstimatedTotal = estimatedTotal,
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow
        };

        const string sql = $@"
            INSERT INTO quotations ({SelectColumns})
            VALUES (@id, @projectId, @estimatedTotal, @createdBy, @createdAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", quotation.Id);
        AddParameter(command, "@projectId", quotation.ProjectId);
        AddParameter(command, "@estimatedTotal", quotation.EstimatedTotal);
        AddParameter(command, "@createdBy", quotation.CreatedBy);
        AddParameter(command, "@createdAt", quotation.CreatedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Returned from what was just built rather than read back: the row is
        // exactly these five values, and a SELECT would cost a round trip to
        // learn nothing new.
        return quotation;
    }

    public async Task<IReadOnlyList<Quotation>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Newest first, so the caller's first element is the project's current
        // estimate. id is the tiebreaker only to make the order total — at
        // DATETIME(6) precision two rows sharing a created_at would take a
        // clock collision to produce.
        const string sql = $@"
            SELECT {SelectColumns}
            FROM quotations
            WHERE project_id = @projectId
            ORDER BY created_at DESC, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var rows = new List<Quotation>();

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

    private static Quotation Map(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        // GetDecimal, not GetDouble: the column is DECIMAL(15,2) and reading it
        // through a binary float would reintroduce exactly the drift the column
        // type was chosen to avoid.
        EstimatedTotal = reader.GetDecimal(reader.GetOrdinal("estimated_total")),
        CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
        // MySQL's DATETIME has no timezone attached, so MySqlConnector reads the
        // value back as DateTimeKind.Unspecified — it cannot know the column
        // stores UTC. System.Text.Json then serializes that without a Z suffix,
        // and the browser reads a no-suffix ISO string as local time, so a
        // Colombo caller would see the quotation dated 5h 30m off after a
        // reload. Every write path here uses DateTime.UtcNow, so stamping the
        // read Kind as Utc restores the invariant the write side already keeps.
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("created_at")), DateTimeKind.Utc)
    };
}
