using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>Data access for <c>quotations</c>.</summary>
public interface IQuotationRepository
{
    /// <summary>
    /// Stores a new cost estimate against a project and returns it as written
    /// (AC-1).
    /// </summary>
    /// <remarks>
    /// Always inserts — it never updates a project's existing quotation. A project
    /// can be re-quoted, and the older figures are the record of what the Client
    /// was told before, so they are kept rather than overwritten.
    /// <para>
    /// There is deliberately no check that the project exists: projects belong to
    /// the Project Service, and this service may not query its database. Verifying
    /// one would mean a synchronous call that makes quoting fail whenever that
    /// service is down. The id is recorded as given.
    /// </para>
    /// </remarks>
    Task<Quotation> CreateAsync(
        Guid projectId,
        decimal estimatedTotal,
        Guid createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every quotation raised for a project, newest first — so the first element
    /// is the project's current estimate and the rest are its history.
    /// </summary>
    /// <remarks>
    /// An empty list means the project has never been quoted, which is a normal
    /// state rather than an error: the Client's view says so instead of 404ing,
    /// since the project itself may well exist.
    /// </remarks>
    Task<IReadOnlyList<Quotation>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
