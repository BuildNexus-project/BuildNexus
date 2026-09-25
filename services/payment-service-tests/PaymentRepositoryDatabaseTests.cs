using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;
using MySqlConnector;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// Recording a payment against an invoice, against the real engine (US-16).
/// </summary>
/// <remarks>
/// The cap and the Paid transition both turn on an outstanding amount MySQL
/// computes, inside a transaction holding a row lock on the invoice — so these
/// run against a real database rather than a stub, which could not show either.
/// </remarks>
[Collection(PaymentDatabaseCollection.Name)]
public class PaymentRepositoryDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public PaymentRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------------- AC-1: the cap ----

    [Fact]
    public async Task A_payment_larger_than_the_outstanding_amount_is_rejected()
    {
        // AC-1, the whole bullet.
        var invoice = await InvoiceFor("d01", 1_000m);

        var result = await Record(invoice, 1_000.01m);

        Assert.Equal(PaymentRecordingOutcome.ExceedsOutstanding, result.Outcome);
        Assert.Null(result.Payment);
        // And nothing was written — a rejected payment must not reduce the balance.
        Assert.Empty(await _fixture.PaymentRepository.ListForInvoiceAsync(invoice));
    }

    [Fact]
    public async Task A_rejected_payment_reports_what_is_actually_outstanding()
    {
        // So the screen can tell the Client what they may pay instead of only
        // that they were refused.
        var invoice = await InvoiceFor("d02", 1_000m);
        await Record(invoice, 400m);

        var result = await Record(invoice, 700m);

        Assert.Equal(PaymentRecordingOutcome.ExceedsOutstanding, result.Outcome);
        Assert.Equal(600m, result.OutstandingAmount);
    }

    [Fact]
    public async Task The_cap_is_the_remaining_balance_not_the_invoice_total()
    {
        // The case a single-payment implementation gets wrong: 700 is under the
        // 1,000 invoice but over the 600 still owed after the first payment.
        var invoice = await InvoiceFor("d03", 1_000m);
        await Record(invoice, 400m);

        Assert.Equal(PaymentRecordingOutcome.ExceedsOutstanding, (await Record(invoice, 700m)).Outcome);
        Assert.Equal(PaymentRecordingOutcome.Recorded, (await Record(invoice, 600m)).Outcome);
    }

    [Fact]
    public async Task A_payment_exactly_equal_to_the_outstanding_amount_is_allowed()
    {
        // The boundary AC-1 turns on: "cannot exceed" means strictly greater is
        // refused, and the equal case is the one that settles the invoice.
        var invoice = await InvoiceFor("d04", 1_000m);

        Assert.Equal(PaymentRecordingOutcome.Recorded, (await Record(invoice, 1_000m)).Outcome);
    }

    [Fact]
    public async Task A_settled_invoice_refuses_a_further_payment()
    {
        var invoice = await InvoiceFor("d05", 500m);
        await Record(invoice, 500m);

        var result = await Record(invoice, 1m);

        // Its own answer rather than "exceeds outstanding": the Client has
        // already paid this, which is a different thing to say.
        Assert.Equal(PaymentRecordingOutcome.AlreadyPaid, result.Outcome);
        Assert.Single(await _fixture.PaymentRepository.ListForInvoiceAsync(invoice));
    }

    [Fact]
    public async Task A_payment_against_an_invoice_that_does_not_exist_is_refused()
    {
        var result = await _fixture.PaymentRepository.RecordPaymentAsync(
            Guid.NewGuid(), 100m, Guid.NewGuid());

        Assert.Equal(PaymentRecordingOutcome.InvoiceNotFound, result.Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task The_database_refuses_a_non_positive_payment(decimal amount)
    {
        // A negative payment would silently raise the outstanding amount — a
        // refund dressed as a payment, which this story does not have.
        var invoice = await InvoiceFor("d06", 1_000m);

        await Assert.ThrowsAsync<MySqlException>(() => Record(invoice, amount));
        Assert.Empty(await _fixture.PaymentRepository.ListForInvoiceAsync(invoice));
    }

    // ------------------------------------- AC-2: settling the invoice ----

    [Fact]
    public async Task A_payment_that_fully_covers_the_balance_marks_the_invoice_Paid()
    {
        // AC-2, the whole bullet.
        var invoice = await InvoiceFor("d07", 750m);

        var result = await Record(invoice, 750m);

        Assert.Equal(PaymentRecordingOutcome.Recorded, result.Outcome);
        Assert.Equal(InvoiceStatus.Paid, result.InvoiceStatus);
        Assert.Equal(0m, result.OutstandingAmount);

        // And the invoice itself says so when read back, not just the result.
        Assert.Equal(InvoiceStatus.Paid, (await ReadInvoice(invoice)).Status);
    }

    [Fact]
    public async Task A_settled_invoice_records_when_it_was_settled()
    {
        // ck_invoices_paid_at_matches_status refuses a Paid row with no date, so
        // this also proves the update wrote both columns together.
        var invoice = await InvoiceFor("d08", 200m);

        await Record(invoice, 200m);

        var stored = await ReadInvoice(invoice);
        Assert.NotNull(stored.PaidAtUtc);
        Assert.Equal(DateTimeKind.Utc, stored.PaidAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task A_partial_payment_leaves_the_invoice_Pending()
    {
        var invoice = await InvoiceFor("d09", 1_000m);

        var result = await Record(invoice, 999.99m);

        Assert.Equal(InvoiceStatus.Pending, result.InvoiceStatus);
        Assert.Equal(0.01m, result.OutstandingAmount);
        Assert.Equal(InvoiceStatus.Pending, (await ReadInvoice(invoice)).Status);
    }

    [Fact]
    public async Task Two_partial_payments_that_together_cover_the_balance_settle_the_invoice()
    {
        // The sequence the story turns on: neither payment covers the invoice on
        // its own, and the second one settles it because the first is counted.
        var invoice = await InvoiceFor("d10", 1_000m);

        var first = await Record(invoice, 400m);
        var second = await Record(invoice, 600m);

        Assert.Equal(InvoiceStatus.Pending, first.InvoiceStatus);
        Assert.Equal(600m, first.OutstandingAmount);

        Assert.Equal(InvoiceStatus.Paid, second.InvoiceStatus);
        Assert.Equal(0m, second.OutstandingAmount);
        Assert.Equal(InvoiceStatus.Paid, (await ReadInvoice(invoice)).Status);
    }

    [Fact]
    public async Task Three_partial_payments_draw_the_balance_down_in_step()
    {
        // The outstanding amount is derived from the rows every time, so it keeps
        // up however many payments there are rather than depending on a running
        // total somebody has to maintain.
        var invoice = await InvoiceFor("d11", 900m);

        Assert.Equal(600m, (await Record(invoice, 300m)).OutstandingAmount);
        Assert.Equal(300m, (await Record(invoice, 300m)).OutstandingAmount);
        Assert.Equal(0m, (await Record(invoice, 300m)).OutstandingAmount);

        Assert.Equal(InvoiceStatus.Paid, (await ReadInvoice(invoice)).Status);
    }

    [Fact]
    public async Task Amounts_with_cents_sum_exactly()
    {
        // Three payments that a binary float would not add back to the invoice
        // total, leaving a fraction of a cent outstanding and the invoice
        // stuck Pending forever.
        var invoice = await InvoiceFor("d12", 100m);

        await Record(invoice, 33.33m);
        await Record(invoice, 33.33m);
        var last = await Record(invoice, 33.34m);

        Assert.Equal(0m, last.OutstandingAmount);
        Assert.Equal(InvoiceStatus.Paid, last.InvoiceStatus);
    }

    // ------------------------------------------------- the payment row ----

    [Fact]
    public async Task A_recorded_payment_keeps_its_amount_its_payer_and_its_invoice()
    {
        var invoice = await InvoiceFor("d13", 500m);
        var client = Guid.NewGuid();

        var result = await _fixture.PaymentRepository.RecordPaymentAsync(invoice, 125.75m, client);

        var stored = Assert.Single(await _fixture.PaymentRepository.ListForInvoiceAsync(invoice));
        Assert.Equal(result.Payment!.Id, stored.Id);
        Assert.Equal(invoice, stored.InvoiceId);
        Assert.Equal(125.75m, stored.Amount);
        Assert.Equal(client, stored.PaidBy);
        Assert.Equal(DateTimeKind.Utc, stored.RecordedAtUtc.Kind);
    }

    [Fact]
    public async Task Payments_are_listed_oldest_first_and_only_for_their_own_invoice()
    {
        var invoice = await InvoiceFor("d14", 1_000m);
        var other = await InvoiceFor("d15", 1_000m);

        var first = await Record(invoice, 100m);
        var second = await Record(invoice, 200m);
        await Record(other, 50m);

        var payments = await _fixture.PaymentRepository.ListForInvoiceAsync(invoice);

        Assert.Equal([first.Payment!.Id, second.Payment!.Id], payments.Select(payment => payment.Id));
        Assert.Single(await _fixture.PaymentRepository.ListForInvoiceAsync(other));
    }

    // ---------------------------------------------------------- helpers ----

    /// <summary>An invoice of <paramref name="amount"/> on this run's own project.</summary>
    private async Task<Guid> InvoiceFor(string suffix, decimal amount)
    {
        var invoice = await _fixture.InvoiceRepository.CreateAsync(
            _fixture.ProjectId(suffix), amount, Guid.NewGuid());

        return invoice.Id;
    }

    private Task<PaymentRecordingResult> Record(Guid invoiceId, decimal amount) =>
        _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, amount, Guid.NewGuid());

    private async Task<Invoice> ReadInvoice(Guid invoiceId)
    {
        // Read back through the real repository rather than a bespoke query, so
        // these assertions see exactly what the API would return.
        var invoices = await _fixture.InvoiceRepository.ListForProjectAsync(await ProjectOf(invoiceId));

        return invoices.Single(invoice => invoice.Id == invoiceId);
    }

    /// <summary>The project an invoice belongs to, read straight from the table.</summary>
    private async Task<Guid> ProjectOf(Guid invoiceId)
    {
        await using var connection = new MySqlConnection(PaymentDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT project_id FROM invoices WHERE id = @id;";
        command.Parameters.AddWithValue("@id", invoiceId);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}
