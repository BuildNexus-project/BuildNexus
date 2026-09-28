using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// The financial half of the combined report (US-19 AC-2), as the API reports it: what has
/// been invoiced, collected and is still outstanding across every project.
/// </summary>
/// <remarks>
/// A near-mirror of <see cref="PaymentReportSummary"/>, kept separate for the same reason
/// every other response here is: the wire contract should not move if the model gains a
/// field this report should not expose.
/// <para>
/// The two counts are sent alongside the money so a reader can tell a large total made of
/// one invoice from the same total made of fifty — and so a zero can be read as "nothing
/// billed" rather than "the query found nothing".
/// </para>
/// </remarks>
public class PaymentReportSummaryResponse
{
    public decimal TotalInvoiced { get; set; }

    public decimal TotalCollected { get; set; }

    /// <summary>
    /// Invoiced minus collected, or <c>null</c> when a date range was applied — invoiced and
    /// collected are scoped by different timestamps, so their difference only means
    /// something over the whole ledger.
    /// </summary>
    public decimal? TotalOutstanding { get; set; }

    public int InvoiceCount { get; set; }

    public int PaymentCount { get; set; }

    /// <summary>The inclusive start of the range these totals cover; <c>null</c> when unfiltered.</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>The exclusive end of the range these totals cover; <c>null</c> when unfiltered.</summary>
    public DateTime? ToUtc { get; set; }

    public static PaymentReportSummaryResponse From(PaymentReportSummary summary) => new()
    {
        TotalInvoiced = summary.TotalInvoiced,
        TotalCollected = summary.TotalCollected,
        TotalOutstanding = summary.TotalOutstanding,
        InvoiceCount = summary.InvoiceCount,
        PaymentCount = summary.PaymentCount,
        FromUtc = summary.FromUtc,
        ToUtc = summary.ToUtc
    };
}
