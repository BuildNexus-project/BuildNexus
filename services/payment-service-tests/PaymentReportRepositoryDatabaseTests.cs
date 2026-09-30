namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// <see cref="Data.PaymentReportRepository"/> against real MySQL — the invoiced and collected
/// aggregates the payment report is built from, and the date range that narrows them
/// (US-19 AC-2).
/// </summary>
/// <remarks>
/// The reason this cannot be a stub: the report sums invoices and payments in two separate
/// queries precisely so that a join does not multiply each invoice's amount by its number of
/// payments. That failure mode is invisible until an invoice actually has two payments
/// against it in a real table, which is what the fan-out test below sets up.
/// <para>
/// The totals are portfolio-wide, so a development database holding other runs' rows would
/// make an absolute assertion meaningless. Every test here measures a <em>delta</em> against
/// a baseline taken first, or scopes itself to a time window that only its own writes fall
/// in — the database collection serialises these classes, so nothing else is writing
/// meanwhile.
/// </para>
/// <para>Needs <c>payment-db</c> running — see <see cref="PaymentDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PaymentDatabaseCollection.Name)]
public class PaymentReportRepositoryDatabaseTests
{
    private static readonly Guid Staff = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly Guid PayingClient = Guid.Parse("33333333-0000-4000-8000-000000000003");

    private readonly PaymentDatabaseFixture _fixture;

    public PaymentReportRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Invoiced_is_the_sum_of_every_invoice_raised()
    {
        var before = await _fixture.PaymentReportRepository.GetSummaryAsync();
        var projectId = _fixture.ProjectId("cd1");

        await _fixture.InvoiceRepository.CreateAsync(projectId, 1_000_000m, Staff);
        await _fixture.InvoiceRepository.CreateAsync(projectId, 250_000m, Staff);

        var after = await _fixture.PaymentReportRepository.GetSummaryAsync();

        Assert.Equal(1_250_000m, after.TotalInvoiced - before.TotalInvoiced);
        Assert.Equal(2, after.InvoiceCount - before.InvoiceCount);
    }

    [Fact]
    public async Task Collected_counts_part_payments_not_settled_invoices()
    {
        // The distinction that matters: this invoice is part paid, so it is still Pending and
        // would contribute nothing to a total built from invoice status — yet real money has
        // come in. Summing the payment rows is what makes the figure honest.
        var before = await _fixture.PaymentReportRepository.GetSummaryAsync();
        var projectId = _fixture.ProjectId("cd2");

        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 1_000_000m, Staff);
        await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 400_000m, PayingClient);

        var after = await _fixture.PaymentReportRepository.GetSummaryAsync();

        Assert.Equal(1_000_000m, after.TotalInvoiced - before.TotalInvoiced);
        Assert.Equal(400_000m, after.TotalCollected - before.TotalCollected);
        Assert.Equal(1, after.PaymentCount - before.PaymentCount);
    }

    [Fact]
    public async Task Two_payments_on_one_invoice_do_not_inflate_the_invoiced_total()
    {
        // The fan-out this repository's two-query shape exists to prevent. Summed across a
        // join, this invoice's 900,000 would be counted twice — 1,800,000 invoiced against
        // 900,000 collected, and a report claiming the project owes money it does not.
        var before = await _fixture.PaymentReportRepository.GetSummaryAsync();
        var projectId = _fixture.ProjectId("cd3");

        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 900_000m, Staff);
        await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 500_000m, PayingClient);
        await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 400_000m, PayingClient);

        var after = await _fixture.PaymentReportRepository.GetSummaryAsync();

        Assert.Equal(900_000m, after.TotalInvoiced - before.TotalInvoiced);
        Assert.Equal(900_000m, after.TotalCollected - before.TotalCollected);
        Assert.Equal(1, after.InvoiceCount - before.InvoiceCount);
        Assert.Equal(2, after.PaymentCount - before.PaymentCount);
    }

    [Fact]
    public async Task Outstanding_is_invoiced_less_collected_when_unfiltered()
    {
        var before = await _fixture.PaymentReportRepository.GetSummaryAsync();
        var projectId = _fixture.ProjectId("cd4");

        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 800_000m, Staff);
        await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 300_000m, PayingClient);

        var after = await _fixture.PaymentReportRepository.GetSummaryAsync();

        Assert.NotNull(after.TotalOutstanding);
        Assert.NotNull(before.TotalOutstanding);
        Assert.Equal(500_000m, after.TotalOutstanding.Value - before.TotalOutstanding.Value);
        // And it stays the arithmetic difference of the two totals it is derived from.
        Assert.Equal(after.TotalInvoiced - after.TotalCollected, after.TotalOutstanding.Value);
    }

    [Fact]
    public async Task A_date_range_narrows_both_totals_to_what_happened_inside_it()
    {
        // The window opens after an earlier invoice and closes after a later one, so the
        // earlier row is excluded by the lower bound alone.
        var projectId = _fixture.ProjectId("cd5");
        await _fixture.InvoiceRepository.CreateAsync(projectId, 700_000m, Staff);

        var windowOpens = DateTime.UtcNow;

        var inside = await _fixture.InvoiceRepository.CreateAsync(projectId, 200_000m, Staff);
        await _fixture.PaymentRepository.RecordPaymentAsync(inside.Id, 120_000m, PayingClient);

        var windowCloses = DateTime.UtcNow.AddSeconds(1);

        var summary = await _fixture.PaymentReportRepository.GetSummaryAsync(windowOpens, windowCloses);

        // Only the rows written inside the window. Absolute, not a delta: nothing else is
        // writing to this database while the collection holds it.
        Assert.Equal(200_000m, summary.TotalInvoiced);
        Assert.Equal(120_000m, summary.TotalCollected);
        Assert.Equal(1, summary.InvoiceCount);
        Assert.Equal(1, summary.PaymentCount);
    }

    [Fact]
    public async Task A_filtered_summary_reports_no_outstanding_total_and_echoes_its_range()
    {
        // Invoiced is scoped by when an invoice was raised, collected by when a payment was
        // recorded. Their difference inside a window can be negative and means nothing, so the
        // repository declines to state it rather than publishing a figure that reads as debt.
        var projectId = _fixture.ProjectId("cd6");
        var windowOpens = DateTime.UtcNow;
        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 100_000m, Staff);
        await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 100_000m, PayingClient);
        var windowCloses = DateTime.UtcNow.AddSeconds(1);

        var summary = await _fixture.PaymentReportRepository.GetSummaryAsync(windowOpens, windowCloses);

        Assert.Null(summary.TotalOutstanding);
        Assert.Equal(windowOpens, summary.FromUtc);
        Assert.Equal(windowCloses, summary.ToUtc);
    }

    [Fact]
    public async Task One_bound_on_its_own_still_filters_and_still_suppresses_outstanding()
    {
        var projectId = _fixture.ProjectId("cd7");
        var windowOpens = DateTime.UtcNow;
        await _fixture.InvoiceRepository.CreateAsync(projectId, 50_000m, Staff);

        var fromOnly = await _fixture.PaymentReportRepository.GetSummaryAsync(fromUtc: windowOpens);

        Assert.Equal(50_000m, fromOnly.TotalInvoiced);
        Assert.Null(fromOnly.TotalOutstanding);
        Assert.Equal(windowOpens, fromOnly.FromUtc);
        Assert.Null(fromOnly.ToUtc);
    }

    [Fact]
    public async Task A_window_with_nothing_in_it_is_zeros_rather_than_nulls()
    {
        // A quiet month is a real answer. COALESCE is what keeps SUM over no rows from
        // arriving as NULL and pushing that decision onto callers.
        var future = DateTime.UtcNow.AddYears(5);

        var summary = await _fixture.PaymentReportRepository.GetSummaryAsync(future, future.AddDays(1));

        Assert.Equal(0m, summary.TotalInvoiced);
        Assert.Equal(0m, summary.TotalCollected);
        Assert.Equal(0, summary.InvoiceCount);
        Assert.Equal(0, summary.PaymentCount);
    }
}
