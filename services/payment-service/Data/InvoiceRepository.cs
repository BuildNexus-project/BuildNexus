using System.Data.Common;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET data access for <c>invoices</c>. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class InvoiceRepository : IInvoiceRepository
{
    private const string SelectColumns =
        "id, project_id, amount, status, created_by, created_at, paid_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public InvoiceRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Invoice> CreateAsync(
        Guid projectId,
        decimal amount,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Amount = amount,
            // AC-2: a raised invoice is Pending. Not a parameter — nothing may
            // create one already settled.
            Status = InvoiceStatus.Pending,
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            PaidAtUtc = null
        };

        const string sql = $@"
            INSERT INTO invoices ({SelectColumns})
            VALUES (@id, @projectId, @amount, @status, @createdBy, @createdAt, @paidAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", invoice.Id);
        AddParameter(command, "@projectId", invoice.ProjectId);
        AddParameter(command, "@amount", invoice.Amount);
        // Persisted as the enum's own name, which is also what the CHECK
        // constraint allows.
        AddParameter(command, "@status", invoice.Status.ToString());
        AddParameter(command, "@createdBy", invoice.CreatedBy);
        AddParameter(command, "@createdAt", invoice.CreatedAtUtc);
        AddParameter(command, "@paidAt", DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return invoice;
    }

    public async Task<IReadOnlyList<Invoice>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM invoices
            WHERE project_id = @projectId
            ORDER BY created_at DESC, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var rows = new List<Invoice>();

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

    private static Invoice Map(DbDataReader reader)
    {
        var paidAt = reader.GetOrdinal("paid_at");

        return new Invoice
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
            // GetDecimal, not GetDouble — the column is DECIMAL(15,2) and a binary
            // float on the path would reintroduce the drift it was chosen to avoid.
            Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
            // Persisted as its own name, so the round trip is a plain enum parse
            // with no int-to-name mapping to keep in sync.
            Status = Enum.Parse<InvoiceStatus>(reader.GetString(reader.GetOrdinal("status"))),
            CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
            // MySQL's DATETIME carries no timezone, so MySqlConnector reads it back
            // as DateTimeKind.Unspecified and System.Text.Json would then serialize
            // it without a Z — which the browser reads as local time, dating the
            // invoice hours off. Every write here uses DateTime.UtcNow, so stamping
            // the Kind on read restores the invariant the write side keeps.
            CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("created_at")), DateTimeKind.Utc),
            PaidAtUtc = reader.IsDBNull(paidAt)
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime(paidAt), DateTimeKind.Utc)
        };
    }
}
