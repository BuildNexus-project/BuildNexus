using System.Data;
using System.Data.Common;
using BuildNexus.PaymentService.Messaging;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET data access for <c>payments</c>. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class PaymentRepository : IPaymentRepository
{
    private const string SelectColumns = "id, invoice_id, amount, paid_by, recorded_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public PaymentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PaymentRecordingResult> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        Guid paidBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();

        // ReadCommitted is MySQL's default here rather than its own RepeatableRead
        // only incidentally — what this relies on is the row lock below, not the
        // isolation level.
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        var invoice = await LockInvoiceAsync(connection, transaction, invoiceId, cancellationToken);

        if (invoice is null)
        {
            return PaymentRecordingResult.NotFound();
        }

        var alreadyPaid = await SumPaymentsAsync(connection, transaction, invoiceId, cancellationToken);
        var outstanding = invoice.Value.Amount - alreadyPaid;

        // A settled invoice is its own answer, checked before the cap: telling a
        // Client "that exceeds the outstanding amount" about an invoice they have
        // already paid in full is true but useless.
        if (invoice.Value.Status == InvoiceStatus.Paid || outstanding <= 0m)
        {
            return PaymentRecordingResult.Rejected(
                PaymentRecordingOutcome.AlreadyPaid, outstandingAmount: 0m, invoice.Value.Status);
        }

        // AC-1. Strictly greater: a payment exactly equal to the outstanding
        // amount is the one that settles the invoice, not one to refuse.
        if (amount > outstanding)
        {
            return PaymentRecordingResult.Rejected(
                PaymentRecordingOutcome.ExceedsOutstanding, outstanding, invoice.Value.Status);
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            Amount = amount,
            PaidBy = paidBy,
            RecordedAtUtc = DateTime.UtcNow
        };

        await InsertPaymentAsync(connection, transaction, payment, cancellationToken);

        var remaining = outstanding - amount;
        var status = invoice.Value.Status;

        // AC-2: the payment that takes the balance to zero settles the invoice.
        // Compared against zero rather than against the requested amount, so the
        // rule is "nothing is left owing" — which stays right however many partial
        // payments came before this one.
        if (remaining == 0m)
        {
            await MarkInvoicePaidAsync(
                connection, transaction, invoiceId, payment.RecordedAtUtc, cancellationToken);

            status = InvoiceStatus.Paid;
        }

        // AC-3, written inside the same transaction as the payment. The event and
        // the money it announces commit together or not at all — a publish made
        // after the commit could be lost and leave a payment nobody was told
        // about, and one made before it could announce a payment that rolled back.
        // Sending it is OutboxDispatcher's job, not this path's.
        await OutboxRepository.InsertAsync(
            connection,
            transaction,
            PaymentEvents.Received(payment, invoice.Value.ProjectId, status),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return PaymentRecordingResult.Recorded(payment, remaining, status);
    }

    public async Task<IReadOnlyList<Payment>> ListForInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM payments
            WHERE invoice_id = @invoiceId
            ORDER BY recorded_at, id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@invoiceId", invoiceId);

        var rows = new List<Payment>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    /// <summary>
    /// Reads the invoice's amount and status, holding a write lock on the row for
    /// the rest of the transaction.
    /// </summary>
    /// <remarks>
    /// <c>FOR UPDATE</c> is the load-bearing part of AC-1. Without it, two
    /// payments arriving together would each read the same outstanding amount,
    /// each find itself within the cap, and both commit — leaving the invoice
    /// overpaid by an amount no single request was ever allowed to pay. With it,
    /// the second waits for the first to commit and then reads the balance the
    /// first left behind.
    /// </remarks>
    private static async Task<(decimal Amount, InvoiceStatus Status, Guid ProjectId)?> LockInvoiceAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT amount, status, project_id
            FROM invoices
            WHERE id = @invoiceId
            FOR UPDATE;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@invoiceId", invoiceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            reader.GetDecimal(reader.GetOrdinal("amount")),
            Enum.Parse<InvoiceStatus>(reader.GetString(reader.GetOrdinal("status"))),
            reader.GetGuid(reader.GetOrdinal("project_id")));
    }

    /// <summary>What has already been paid against the invoice.</summary>
    /// <remarks>
    /// Read inside the same transaction as the lock above, so it cannot be a
    /// figure that changed between the check and the write. COALESCE keeps an
    /// invoice with no payments at zero rather than NULL.
    /// </remarks>
    private static async Task<decimal> SumPaymentsAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM payments
            WHERE invoice_id = @invoiceId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@invoiceId", invoiceId);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    private static async Task InsertPaymentAsync(
        DbConnection connection,
        DbTransaction transaction,
        Payment payment,
        CancellationToken cancellationToken)
    {
        const string sql = $@"
            INSERT INTO payments ({SelectColumns})
            VALUES (@id, @invoiceId, @amount, @paidBy, @recordedAt);";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@id", payment.Id);
        AddParameter(command, "@invoiceId", payment.InvoiceId);
        AddParameter(command, "@amount", payment.Amount);
        AddParameter(command, "@paidBy", payment.PaidBy);
        AddParameter(command, "@recordedAt", payment.RecordedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Settles the invoice (AC-2).</summary>
    /// <remarks>
    /// Status and <c>paid_at</c> are written together because
    /// <c>ck_invoices_paid_at_matches_status</c> refuses a Paid row without a
    /// date — US-15 put that constraint there so the two columns cannot drift,
    /// and this is the write it was guarding against.
    /// <para>
    /// The settlement time is the payment's own <c>recorded_at</c>, not a second
    /// <c>UtcNow</c>: the invoice was settled by that payment, at that moment.
    /// </para>
    /// </remarks>
    private static async Task MarkInvoicePaidAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid invoiceId,
        DateTime paidAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE invoices
            SET status  = @status,
                paid_at = @paidAt
            WHERE id = @invoiceId;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@status", InvoiceStatus.Paid.ToString());
        AddParameter(command, "@paidAt", paidAtUtc);
        AddParameter(command, "@invoiceId", invoiceId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Payment Map(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        InvoiceId = reader.GetGuid(reader.GetOrdinal("invoice_id")),
        Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
        PaidBy = reader.GetGuid(reader.GetOrdinal("paid_by")),
        // MySQL's DATETIME carries no timezone, so MySqlConnector reads it back as
        // Unspecified and System.Text.Json would serialize it without a Z — which
        // the browser then reads as local time. Every write here uses UtcNow, so
        // stamping the Kind restores the invariant the write side keeps.
        RecordedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("recorded_at")), DateTimeKind.Utc)
    };
}
