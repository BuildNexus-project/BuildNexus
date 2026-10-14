using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>Read-only query behind the Client dashboard's payments due (US-21).</summary>
/// <remarks>
/// Kept apart from <see cref="IPaymentHistoryRepository"/>: that one answers for a
/// single project the caller names, and this answers across every project the caller
/// owns without naming any.
/// </remarks>
public interface IPaymentDashboardRepository
{
    /// <summary>
    /// Every unsettled invoice on the projects the Client owns, with what has been
    /// paid against each, oldest first (US-21 AC-1).
    /// </summary>
    /// <remarks>
    /// Ownership is this service's own record — the <c>project_owners</c> row the
    /// <c>ProjectCreated</c> consumer planted — so no other service is asked. A
    /// settled invoice is not "due". Oldest first because the invoice a Client has been
    /// carrying longest is the one to pay first. A Client with nothing owing gets an
    /// empty list.
    /// </remarks>
    Task<IReadOnlyList<PaymentDue>> ListPaymentsDueForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default);
}
