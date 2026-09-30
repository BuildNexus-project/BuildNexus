using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The Client's billing view against the real engine (US-17).
/// </summary>
/// <remarks>
/// The figures here have to agree with the rows US-15 and US-16 actually write,
/// and with US-16's own definition of "outstanding" — a screen that showed a
/// different number would offer a Client an amount the pay endpoint then
/// refuses. That agreement is only demonstrable against a real database.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PaymentDatabaseCollection.Name)]
public class PaymentHistoryRepositoryDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public PaymentHistoryRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------ AC-1: invoices and payments ----

    [Fact]
    public async Task A_project_with_no_invoices_is_an_empty_history_not_a_failure()
    {
        // A project that simply has not been billed yet. The screen reads this
        // as "nothing yet", not as an error.
        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(
            _fixture.ProjectId("f01"));

        Assert.Empty(history.Invoices);
        Assert.Equal(0m, history.TotalInvoiced);
        Assert.Equal(0m, history.TotalPaid);
        Assert.Equal(0m, history.OutstandingBalance);
    }

    [Fact]
    public async Task Every_invoice_on_the_project_is_listed_with_its_status()
    {
        // AC-1: all invoices, with status.
        var projectId = _fixture.ProjectId("f02");
        await InvoiceOn(projectId, 500m);
        var settled = await InvoiceOn(projectId, 300m);
        await Pay(settled, 300m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal(2, history.Invoices.Count);
        Assert.Contains(history.Invoices, entry => entry.Invoice.Status == InvoiceStatus.Pending);
        Assert.Contains(history.Invoices, entry => entry.Invoice.Status == InvoiceStatus.Paid);
    }

    [Fact]
    public async Task Invoices_come_back_most_recent_first()
    {
        // AC-1's ordering.
        var projectId = _fixture.ProjectId("f03");
        var first = await InvoiceOn(projectId, 100m);
        var second = await InvoiceOn(projectId, 200m);
        var third = await InvoiceOn(projectId, 300m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal([third, second, first], history.Invoices.Select(entry => entry.Invoice.Id));
    }

    [Fact]
    public async Task Each_invoice_carries_the_payments_made_against_it_oldest_first()
    {
        // AC-1's "linked payments" — and in the order the Client made them.
        var projectId = _fixture.ProjectId("f04");
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Pay(invoice, 200m);
        await Pay(invoice, 300m);

        var entry = Assert.Single((await _fixture.PaymentHistoryRepository
            .GetForProjectAsync(projectId)).Invoices);

        Assert.Equal([200m, 300m], entry.Payments.Select(payment => payment.Amount));
        Assert.Equal(500m, entry.AmountPaid);
    }

    [Fact]
    public async Task A_payment_is_listed_only_against_the_invoice_it_was_made_against()
    {
        // The grouping, not just the total: two invoices on one project must not
        // pool their payments.
        var projectId = _fixture.ProjectId("f05");
        var paid = await InvoiceOn(projectId, 500m);
        var untouched = await InvoiceOn(projectId, 400m);
        await Pay(paid, 100m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Single(Entry(history, paid).Payments);
        Assert.Empty(Entry(history, untouched).Payments);
    }

    [Fact]
    public async Task One_projects_history_never_shows_anothers()
    {
        var mine = _fixture.ProjectId("f06");
        var other = _fixture.ProjectId("f07");
        await InvoiceOn(mine, 100m);
        await Pay(await InvoiceOn(other, 900m), 900m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(mine);

        Assert.Single(history.Invoices);
        Assert.Equal(100m, history.TotalInvoiced);
        Assert.Equal(0m, history.TotalPaid);
    }

    // ------------------------------- AC-2: the outstanding balance ----

    [Fact]
    public async Task The_balance_is_what_is_still_owed_across_every_invoice()
    {
        // AC-2. 1000 billed, 400 paid, so 600 owed — across two invoices.
        var projectId = _fixture.ProjectId("f08");
        var first = await InvoiceOn(projectId, 600m);
        var second = await InvoiceOn(projectId, 400m);
        await Pay(first, 250m);
        await Pay(second, 150m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal(1_000m, history.TotalInvoiced);
        Assert.Equal(400m, history.TotalPaid);
        Assert.Equal(600m, history.OutstandingBalance);
    }

    [Fact]
    public async Task A_settled_invoice_contributes_nothing_to_the_balance()
    {
        // AC-2 describes the balance over unpaid and partially paid invoices; a
        // settled one is zero, so it needs no special case.
        var projectId = _fixture.ProjectId("f09");
        var settled = await InvoiceOn(projectId, 500m);
        await Pay(settled, 500m);
        await InvoiceOn(projectId, 200m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal(0m, Entry(history, settled).OutstandingAmount);
        Assert.Equal(200m, history.OutstandingBalance);
    }

    [Fact]
    public async Task A_fully_settled_project_owes_nothing()
    {
        var projectId = _fixture.ProjectId("f10");
        await Pay(await InvoiceOn(projectId, 300m), 300m);
        await Pay(await InvoiceOn(projectId, 700m), 700m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal(0m, history.OutstandingBalance);
        Assert.All(history.Invoices, entry => Assert.Equal(InvoiceStatus.Paid, entry.Invoice.Status));
    }

    [Fact]
    public async Task The_balance_falls_as_payments_are_recorded()
    {
        // AC-2's "updates immediately" at the data layer: the figure is derived
        // on every read, so a payment recorded a moment ago is already in it.
        var projectId = _fixture.ProjectId("f11");
        var invoice = await InvoiceOn(projectId, 1_000m);

        Assert.Equal(1_000m, await BalanceOf(projectId));

        await Pay(invoice, 400m);
        Assert.Equal(600m, await BalanceOf(projectId));

        await Pay(invoice, 600m);
        Assert.Equal(0m, await BalanceOf(projectId));
    }

    [Fact]
    public async Task The_per_invoice_remainder_matches_what_the_pay_endpoint_would_accept()
    {
        // The agreement that matters: this screen's figure and US-16's cap are
        // the same arithmetic, so a Client offered 600 is not then refused for
        // entering 600.
        var projectId = _fixture.ProjectId("f12");
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Pay(invoice, 355.55m);

        var shown = Entry(await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId), invoice)
            .OutstandingAmount;

        // Paying exactly what the history says is outstanding settles it.
        var result = await _fixture.PaymentRepository.RecordPaymentAsync(invoice, shown, Guid.NewGuid());

        Assert.Equal(Data.PaymentRecordingOutcome.Recorded, result.Outcome);
        Assert.Equal(0m, result.OutstandingAmount);
    }

    [Fact]
    public async Task Amounts_with_cents_sum_exactly_across_the_project()
    {
        // Three invoices a binary float would not add back to 100.00, leaving a
        // fraction of a cent owing on a project that is fully settled.
        var projectId = _fixture.ProjectId("f13");
        await Pay(await InvoiceOn(projectId, 33.33m), 33.33m);
        await Pay(await InvoiceOn(projectId, 33.33m), 33.33m);
        await Pay(await InvoiceOn(projectId, 33.34m), 33.34m);

        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal(100m, history.TotalInvoiced);
        Assert.Equal(100m, history.TotalPaid);
        Assert.Equal(0m, history.OutstandingBalance);
    }

    [Fact]
    public async Task A_stored_timestamp_reads_back_as_UTC()
    {
        // Otherwise the browser reads a no-suffix ISO string as local time and
        // dates every invoice and payment hours off.
        var projectId = _fixture.ProjectId("f14");
        var invoice = await InvoiceOn(projectId, 100m);
        await Pay(invoice, 50m);

        var entry = Assert.Single((await _fixture.PaymentHistoryRepository
            .GetForProjectAsync(projectId)).Invoices);

        Assert.Equal(DateTimeKind.Utc, entry.Invoice.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(entry.Payments).RecordedAtUtc.Kind);
    }

    // ---------------------------------------------------------- helpers ----

    private async Task<Guid> InvoiceOn(Guid projectId, decimal amount) =>
        (await _fixture.InvoiceRepository.CreateAsync(projectId, amount, Guid.NewGuid())).Id;

    private Task Pay(Guid invoiceId, decimal amount) =>
        _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, amount, Guid.NewGuid());

    private async Task<decimal> BalanceOf(Guid projectId) =>
        (await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId)).OutstandingBalance;

    private static InvoiceWithPayments Entry(ProjectPaymentHistory history, Guid invoiceId) =>
        history.Invoices.Single(entry => entry.Invoice.Id == invoiceId);
}
