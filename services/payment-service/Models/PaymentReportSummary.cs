namespace BuildNexus.PaymentService.Models;

/// <summary>
/// The financial side of the combined report (US-19 AC-2): what has been billed across
/// every project, what has actually come in, and what is still owed.
/// </summary>
/// <remarks>
/// Portfolio-wide totals, not per project — AC-2 asks for the amounts "across all
/// projects", and a reader who wants one project's detail has the Client billing view and
/// the invoice listings for that.
/// <para>
/// Every figure is summed from the <c>invoices</c> and <c>payments</c> rows on the fly.
/// Nothing stores a running total, so a report cannot drift from the rows behind it.
/// </para>
/// </remarks>
public class PaymentReportSummary
{
    /// <summary>
    /// The total value of every invoice raised: <c>SUM(invoices.amount)</c>.
    /// </summary>
    public required decimal TotalInvoiced { get; init; }

    /// <summary>
    /// The total actually received: <c>SUM(payments.amount)</c>.
    /// </summary>
    /// <remarks>
    /// Summed from the payment rows rather than from invoices marked <c>Paid</c>. A
    /// part-paid invoice is a real thing here — the payments table has no "must settle the
    /// whole invoice" rule — so counting only settled invoices would under-report what has
    /// been collected, sometimes by a lot.
    /// </remarks>
    public required decimal TotalCollected { get; init; }

    /// <summary>
    /// <see cref="TotalInvoiced"/> minus <see cref="TotalCollected"/>, or <c>null</c> when
    /// a date range was applied.
    /// </summary>
    /// <remarks>
    /// Null under a filter on purpose. Invoiced is scoped by when an invoice was raised and
    /// collected by when a payment was recorded, so a payment inside the window settling an
    /// invoice raised before it would make the difference of the two sums meaningless — and
    /// with enough of them, negative. "What is still owed" is a question about the whole
    /// ledger, not about a window, so the honest answer inside one is to not answer.
    /// </remarks>
    public decimal? TotalOutstanding { get; init; }

    /// <summary>How many invoices the invoiced total is made of.</summary>
    public required int InvoiceCount { get; init; }

    /// <summary>How many payment records the collected total is made of.</summary>
    public required int PaymentCount { get; init; }

    /// <summary>The inclusive start of the range the totals cover, or <c>null</c> when unfiltered.</summary>
    public DateTime? FromUtc { get; init; }

    /// <summary>The exclusive end of the range the totals cover, or <c>null</c> when unfiltered.</summary>
    public DateTime? ToUtc { get; init; }
}
