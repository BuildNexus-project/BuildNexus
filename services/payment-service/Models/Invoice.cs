namespace BuildNexus.PaymentService.Models;

/// <summary>
/// The two states an invoice can be in, exactly as US-15's AC-2 names them.
/// </summary>
/// <remarks>
/// Kept as an enum for the C# side; persisted as its own name
/// (<c>"Pending"</c>, <c>"Paid"</c>) rather than an integer, so a DBA reading
/// the table can see what a row means and so inserting a member later cannot
/// silently change what the existing rows mean.
/// </remarks>
public enum InvoiceStatus
{
    /// <summary>Raised and not yet settled. Every invoice starts here.</summary>
    Pending,

    /// <summary>Settled.</summary>
    Paid
}

/// <summary>
/// One invoice raised against a project (US-15, AC-2). Owned entirely by the
/// Payment Service — no other service sees or joins against these rows.
/// </summary>
public class Invoice
{
    /// <summary>AC-2's unique ID.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The project being billed. Not a foreign key across a service boundary —
    /// the project id is stored as a plain column.
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// AC-2's amount. <see cref="decimal"/> throughout, matching the column's
    /// <c>DECIMAL(15,2)</c>.
    /// </summary>
    public required decimal Amount { get; init; }

    public required InvoiceStatus Status { get; init; }

    /// <summary>
    /// Who raised it: the Project Manager or Admin who asked for it, or — for one
    /// raised automatically — the Project Manager whose decision triggered it.
    /// </summary>
    public required Guid CreatedBy { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// When it was settled, or <c>null</c> while it is still
    /// <see cref="InvoiceStatus.Pending"/>.
    /// </summary>
    public DateTime? PaidAtUtc { get; init; }
}
