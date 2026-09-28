using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// What an <c>InvoiceGenerated</c> event carries (US-24): which invoice was raised, against
/// which project, and for how much.
/// </summary>
/// <remarks>
/// <see cref="SourceEventId"/> is what tells a reader <em>why</em> the invoice exists: null
/// for one a Project Manager raised by hand, and the id of the <c>ConstructionStarted</c>
/// event for one the consumer raised automatically. Carrying it means the topic explains its
/// own causation rather than leaving a reader to correlate timestamps.
/// <para>
/// No status field. A freshly raised invoice is always <c>Pending</c> — nothing may create one
/// already settled — so a status here would be a constant dressed up as information, and a
/// consumer might reasonably read it as current long after a payment had arrived.
/// </para>
/// </remarks>
public sealed class InvoiceGeneratedPayload
{
    public required Guid InvoiceId { get; init; }

    public required Guid ProjectId { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>Who raised it — a Project Manager or Admin by hand, or the service itself.</summary>
    public required Guid CreatedBy { get; init; }

    /// <summary>
    /// The <c>ConstructionStarted</c> event that caused this invoice, or <c>null</c> when a
    /// person raised it directly.
    /// </summary>
    public Guid? SourceEventId { get; init; }

    /// <summary>
    /// When the invoice was raised, off the row rather than the clock, so the event's
    /// <c>occurredAt</c> and the stored <c>created_at</c> cannot disagree.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    public static InvoiceGeneratedPayload From(Invoice invoice) => new()
    {
        InvoiceId = invoice.Id,
        ProjectId = invoice.ProjectId,
        Amount = invoice.Amount,
        CreatedBy = invoice.CreatedBy,
        SourceEventId = invoice.SourceEventId,
        GeneratedAt = EventTimestamp.AsUtc(invoice.CreatedAtUtc)
    };
}
