using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An <see cref="IPaymentReportRepository"/> that records the range it was asked for and
/// answers with whatever summary a test sets, so <see cref="Controllers.ReportsController"/>
/// can be checked without MySQL.
/// </summary>
/// <remarks>
/// The aggregate SQL — the two separate sums, the optional bounds, the fan-out this avoids —
/// is what <see cref="PaymentReportRepositoryDatabaseTests"/> exercises against the real
/// engine. What this stands in for is the controller's own validation and mapping.
/// </remarks>
public sealed class FakePaymentReportRepository : IPaymentReportRepository
{
    /// <summary>One entry per call, in order: the bounds the controller passed down.</summary>
    public List<RangeCall> Calls { get; } = [];

    /// <summary>What the repository answers. An empty ledger by default.</summary>
    public PaymentReportSummary Summary { get; set; } = new()
    {
        TotalInvoiced = 0m,
        TotalCollected = 0m,
        TotalOutstanding = 0m,
        InvoiceCount = 0,
        PaymentCount = 0
    };

    public Task<PaymentReportSummary> GetSummaryAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new RangeCall(fromUtc, toUtc));
        return Task.FromResult(Summary);
    }

    public readonly record struct RangeCall(DateTime? FromUtc, DateTime? ToUtc);
}
