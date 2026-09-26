using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Read-only aggregate queries over <c>invoices</c> and <c>payments</c> for the
/// Client's billing view (US-17).
/// </summary>
/// <remarks>
/// Its own repository rather than a method bolted onto
/// <see cref="IInvoiceRepository"/> or <see cref="IPaymentRepository"/>: the
/// read spans both tables and belongs to neither, the same way
/// <c>DesignReportRepository</c> sits beside the two repositories its report
/// reads from. Nothing here writes.
/// </remarks>
public interface IPaymentHistoryRepository
{
    /// <summary>
    /// A project's invoices, most recent first, each with the payments made
    /// against it, plus the balance still outstanding across all of them
    /// (AC-1 and AC-2).
    /// </summary>
    /// <remarks>
    /// One answer rather than two endpoints: AC-2 requires the balance on the
    /// same view as the history, and computing it from a separate request would
    /// let the two disagree if a payment landed between them.
    /// <para>
    /// A project with no invoices is a real answer — an empty list at a zero
    /// balance — not a 404. The project may well exist and simply not have been
    /// billed yet, and this service cannot tell the difference anyway.
    /// </para>
    /// </remarks>
    Task<ProjectPaymentHistory> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
