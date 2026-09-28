using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Aggregate reads over this service's own invoices and payments, for the financial half of
/// the combined report (US-19).
/// </summary>
/// <remarks>
/// Separate from <see cref="IPaymentHistoryRepository"/>, which answers about one project
/// for the Client who owns it. This one sweeps the whole ledger for an Admin or a Project
/// Manager, so keeping them apart means the report's queries cannot creep into the
/// Client-facing path and US-17's contract does not move.
/// <para>
/// Reads only. This story writes nothing — it reports on what US-15 through US-17 already
/// store. The construction half of the report is Construction Service's own endpoint over
/// its own schema; neither service touches the other's database.
/// </para>
/// </remarks>
public interface IPaymentReportRepository
{
    /// <summary>
    /// Invoiced, collected and outstanding totals across every project, optionally narrowed
    /// to a date range (AC-2).
    /// </summary>
    /// <remarks>
    /// <paramref name="fromUtc"/> is inclusive and <paramref name="toUtc"/> exclusive, so a
    /// caller asking for a month gets that month without the first instant of the next one
    /// — and two adjacent ranges neither overlap nor drop a row between them.
    /// <para>
    /// The range applies to when each figure happened: an invoice by
    /// <c>invoices.created_at</c>, a payment by <c>payments.recorded_at</c>. Because those
    /// are different clocks on different rows, the outstanding total is only meaningful
    /// unfiltered, and comes back <c>null</c> whenever either bound is given — see
    /// <see cref="PaymentReportSummary.TotalOutstanding"/>.
    /// </para>
    /// <para>
    /// An empty ledger is zeros, not <c>null</c> and not an error: "nothing has been billed
    /// yet" is a real state of a young portfolio, and a report that failed on it would have
    /// the caller unable to distinguish that from a fault.
    /// </para>
    /// </remarks>
    Task<PaymentReportSummary> GetSummaryAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);
}
