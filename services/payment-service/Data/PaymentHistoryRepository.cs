using System.Data.Common;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET aggregate queries for the Client's billing view. Direct SQL only —
/// no ORM. Every value reaches MySQL as a bound parameter.
/// </summary>
public class PaymentHistoryRepository : IPaymentHistoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PaymentHistoryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ProjectPaymentHistory> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();

        var invoices = await ReadInvoicesAsync(connection, projectId, cancellationToken);
        var paymentsByInvoice = await ReadPaymentsAsync(connection, projectId, cancellationToken);

        // Two queries and a join in memory, rather than one query per invoice.
        // A project's invoices and its payments are both small and both already
        // filtered to this project, so the alternative — a round trip per
        // invoice — would cost N queries to answer the same thing.
        var composed = invoices
            .Select(invoice =>
            {
                var payments = paymentsByInvoice.TryGetValue(invoice.Id, out var rows)
                    ? rows
                    : (IReadOnlyList<Payment>)[];

                var amountPaid = payments.Sum(payment => payment.Amount);

                return new InvoiceWithPayments
                {
                    Invoice = invoice,
                    Payments = payments,
                    AmountPaid = amountPaid,
                    OutstandingAmount = invoice.Amount - amountPaid
                };
            })
            .ToList();

        return new ProjectPaymentHistory
        {
            ProjectId = projectId,
            Invoices = composed,
            TotalInvoiced = composed.Sum(entry => entry.Invoice.Amount),
            TotalPaid = composed.Sum(entry => entry.AmountPaid),
            // Summed per invoice rather than as invoiced-minus-paid — see the
            // remarks on ProjectPaymentHistory.OutstandingBalance.
            OutstandingBalance = composed.Sum(entry => entry.OutstandingAmount)
        };
    }

    /// <summary>The project's invoices, most recent first (AC-1).</summary>
    private static async Task<IReadOnlyList<Invoice>> ReadInvoicesAsync(
        DbConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        // id is the tiebreaker only to make the order total — at DATETIME(6)
        // precision two invoices sharing a created_at would take a clock
        // collision to produce.
        const string sql = @"
            SELECT id, project_id, amount, status, created_by, source_event_id, created_at, paid_at
            FROM invoices
            WHERE project_id = @projectId
            ORDER BY created_at DESC, id;";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var rows = new List<Invoice>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapInvoice(reader));
        }

        return rows;
    }

    /// <summary>
    /// Every payment on the project, grouped by the invoice it was made against.
    /// </summary>
    /// <remarks>
    /// Joined to <c>invoices</c> to scope by project — a key inside this
    /// service's own schema, which is the one direction a join may go. Oldest
    /// first within each invoice, so a Client reads their payments in the order
    /// they made them.
    /// </remarks>
    private static async Task<Dictionary<Guid, IReadOnlyList<Payment>>> ReadPaymentsAsync(
        DbConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT p.id, p.invoice_id, p.amount, p.paid_by, p.recorded_at
            FROM payments p
            INNER JOIN invoices i ON i.id = p.invoice_id
            WHERE i.project_id = @projectId
            ORDER BY p.recorded_at, p.id;";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var grouped = new Dictionary<Guid, List<Payment>>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var payment = MapPayment(reader);

            if (!grouped.TryGetValue(payment.InvoiceId, out var rows))
            {
                rows = [];
                grouped[payment.InvoiceId] = rows;
            }

            rows.Add(payment);
        }

        return grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Payment>)pair.Value);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Invoice MapInvoice(DbDataReader reader)
    {
        var paidAt = reader.GetOrdinal("paid_at");
        var sourceEventId = reader.GetOrdinal("source_event_id");

        return new Invoice
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
            // GetDecimal, not GetDouble — these are summed into a balance, so a
            // binary float would compound its drift across the whole project.
            Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
            Status = Enum.Parse<InvoiceStatus>(reader.GetString(reader.GetOrdinal("status"))),
            CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
            SourceEventId = reader.IsDBNull(sourceEventId) ? null : reader.GetGuid(sourceEventId),
            // MySQL's DATETIME carries no timezone, so MySqlConnector reads it back
            // as Unspecified and System.Text.Json would serialize it without a Z —
            // which the browser reads as local time, dating the invoice hours off.
            CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("created_at")), DateTimeKind.Utc),
            PaidAtUtc = reader.IsDBNull(paidAt)
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime(paidAt), DateTimeKind.Utc)
        };
    }

    private static Payment MapPayment(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        InvoiceId = reader.GetGuid(reader.GetOrdinal("invoice_id")),
        Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
        PaidBy = reader.GetGuid(reader.GetOrdinal("paid_by")),
        RecordedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("recorded_at")), DateTimeKind.Utc)
    };
}
