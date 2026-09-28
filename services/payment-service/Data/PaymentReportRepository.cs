using System.Data.Common;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET aggregate queries for the payment reports. Direct SQL only — no ORM. Every value
/// reaches MySQL as a bound parameter.
/// </summary>
public class PaymentReportRepository : IPaymentReportRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PaymentReportRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PaymentReportSummary> GetSummaryAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();

        // Two aggregates rather than one joined query, deliberately. Summing invoices and
        // payments across a join would multiply each invoice's amount by its number of
        // payments — the classic fan-out that silently inflates a financial total. Keeping
        // them apart means each SUM counts each row exactly once.
        var (invoiced, invoiceCount) = await ReadInvoicedAsync(connection, fromUtc, toUtc, cancellationToken);
        var (collected, paymentCount) = await ReadCollectedAsync(connection, fromUtc, toUtc, cancellationToken);

        var filtered = fromUtc is not null || toUtc is not null;

        return new PaymentReportSummary
        {
            TotalInvoiced = invoiced,
            TotalCollected = collected,
            // Only meaningful over the whole ledger — see the model's remarks.
            TotalOutstanding = filtered ? null : invoiced - collected,
            InvoiceCount = invoiceCount,
            PaymentCount = paymentCount,
            FromUtc = fromUtc,
            ToUtc = toUtc
        };
    }

    /// <summary>What has been billed, and over how many invoices.</summary>
    private static async Task<(decimal Total, int Count)> ReadInvoicedAsync(
        DbConnection connection,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        // COALESCE keeps an empty ledger at zero: SUM over no rows is NULL, and a report
        // that answered null for "nothing billed yet" would push the decision onto callers.
        const string sql = @"
            SELECT
                COALESCE(SUM(amount), 0) AS total,
                COUNT(*)                 AS row_count
            FROM invoices
            WHERE (@fromUtc IS NULL OR created_at >= @fromUtc)
              AND (@toUtc   IS NULL OR created_at <  @toUtc);";

        return await ReadTotalAsync(connection, sql, fromUtc, toUtc, cancellationToken);
    }

    /// <summary>What has actually come in, and over how many payment records.</summary>
    private static async Task<(decimal Total, int Count)> ReadCollectedAsync(
        DbConnection connection,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        // Straight off the payments table. Not "invoices where status = 'Paid'": a part
        // payment is a real row here, and counting only settled invoices would under-report
        // what has been collected.
        const string sql = @"
            SELECT
                COALESCE(SUM(amount), 0) AS total,
                COUNT(*)                 AS row_count
            FROM payments
            WHERE (@fromUtc IS NULL OR recorded_at >= @fromUtc)
              AND (@toUtc   IS NULL OR recorded_at <  @toUtc);";

        return await ReadTotalAsync(connection, sql, fromUtc, toUtc, cancellationToken);
    }

    /// <summary>
    /// Runs one of the two aggregates. Both bind the same two optional bounds, so the
    /// parameter handling lives in one place rather than being repeated and drifting.
    /// </summary>
    private static async Task<(decimal Total, int Count)> ReadTotalAsync(
        DbConnection connection,
        string sql,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@fromUtc", fromUtc);
        AddParameter(command, "@toUtc", toUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // An aggregate query always returns one row.
        await reader.ReadAsync(cancellationToken);

        return (
            Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("total"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("row_count"))));
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // A missing bound is a real NULL, which is what the `@x IS NULL OR ...` guards read.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
