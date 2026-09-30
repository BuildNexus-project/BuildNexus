using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// One invoice as the API returns it (US-15, AC-2). A near-mirror of
/// <see cref="Invoice"/>, kept separate so the wire contract does not move if
/// the model gains a field this response should not expose.
/// </summary>
public class InvoiceResponse
{
    /// <summary>AC-2's unique ID.</summary>
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// Serialised as its name (<c>"Pending"</c>, <c>"Paid"</c>) via the
    /// <c>JsonStringEnumConverter</c> registered in <c>Program.cs</c> — kinder to
    /// the React side than an integer, and stable if a member is ever added.
    /// </summary>
    public InvoiceStatus Status { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public static InvoiceResponse From(Invoice invoice) => new()
    {
        Id = invoice.Id,
        ProjectId = invoice.ProjectId,
        Amount = invoice.Amount,
        Status = invoice.Status,
        CreatedBy = invoice.CreatedBy,
        CreatedAtUtc = invoice.CreatedAtUtc,
        PaidAtUtc = invoice.PaidAtUtc
    };
}
