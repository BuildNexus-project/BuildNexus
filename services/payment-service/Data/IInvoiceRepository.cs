using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>Data access for <c>invoices</c>.</summary>
public interface IInvoiceRepository
{
    /// <summary>
    /// Raises a new invoice against a project and returns it as written (AC-2):
    /// a unique id, an amount, and a status of
    /// <see cref="InvoiceStatus.Pending"/>.
    /// </summary>
    /// <remarks>
    /// Always <c>Pending</c> — an invoice is raised before it is settled, and no
    /// caller may declare one paid at the moment it is created.
    /// <para>
    /// There is deliberately no check that the project exists, and none that it
    /// has been quoted. Projects belong to the Project Service, which this
    /// service may not query; and a project can be billed without ever having
    /// been formally estimated.
    /// </para>
    /// </remarks>
    Task<Invoice> CreateAsync(
        Guid projectId,
        decimal amount,
        Guid createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every invoice raised against a project, newest first.
    /// </summary>
    /// <remarks>
    /// An empty list means the project has not been billed yet, which is a normal
    /// state rather than an error.
    /// </remarks>
    Task<IReadOnlyList<Invoice>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
