using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// One quotation as the API returns it (US-15, AC-1). A near-mirror of
/// <see cref="Quotation"/>, kept separate so the wire contract does not move if
/// the model gains a field this response should not expose.
/// </summary>
public class QuotationResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public decimal EstimatedTotal { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public static QuotationResponse From(Quotation quotation) => new()
    {
        Id = quotation.Id,
        ProjectId = quotation.ProjectId,
        EstimatedTotal = quotation.EstimatedTotal,
        CreatedBy = quotation.CreatedBy,
        CreatedAtUtc = quotation.CreatedAtUtc
    };
}
