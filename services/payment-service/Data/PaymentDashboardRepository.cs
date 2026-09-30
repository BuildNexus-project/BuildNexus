using System.Data.Common;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET read for the Client dashboard's payments due. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class PaymentDashboardRepository : IPaymentDashboardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PaymentDashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PaymentDue>> ListPaymentsDueForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Led by project_owners so a Client is only ever shown what they own — the
        // ownership fact this service already holds, checked in the query rather
        // than on each row. The payments are a LEFT JOIN because an invoice nobody
        // has paid against is the ordinary case, and it must stay in with zero paid;
        // COALESCE turns the NULL that SUM gives an empty group into that zero.
        //
        // Grouping by the invoice's own columns keeps the amount from being counted
        // once per payment: each invoice is one group, whatever its number of
        // payments. Status 'Pending' is what "still owing" means — settling the last
        // of an invoice flips it to Paid in the same transaction (US-16 AC-2).
        //
        // Oldest first, `id` breaking a tie between two raised in the same instant so
        // the order is the same on every read.
        const string sql = @"
            SELECT
                i.id                          AS invoice_id,
                i.project_id                  AS project_id,
                i.amount                      AS amount,
                COALESCE(SUM(p.amount), 0)    AS amount_paid,
                i.created_at                  AS raised_at
            FROM project_owners po
            INNER JOIN invoices i ON i.project_id = po.project_id
            LEFT JOIN payments p ON p.invoice_id = i.id
            WHERE po.client_id = @clientId
              AND i.status = 'Pending'
            GROUP BY i.id, i.project_id, i.amount, i.created_at
            ORDER BY i.created_at, i.id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@clientId", clientId);

        var due = new List<PaymentDue>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            due.Add(new PaymentDue
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                InvoiceId = reader.GetGuid(reader.GetOrdinal("invoice_id")),
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                // SUM over DECIMAL is DECIMAL, but COALESCE with the integer literal can
                // surface it as another numeric type; Convert accepts whichever it is.
                AmountPaid = Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("amount_paid"))),
                RaisedAtUtc = reader.GetDateTime(reader.GetOrdinal("raised_at"))
            });
        }

        return due;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
