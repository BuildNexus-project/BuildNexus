using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// What a <c>PaymentReceived</c> event carries (AC-3).
/// </summary>
/// <remarks>
/// Deliberately small — the facts about the payment itself, and the one fact
/// about the invoice that a consumer cannot work out on its own.
/// <para>
/// <see cref="InvoiceStatus"/> is that one: it says whether this payment settled
/// the invoice or merely reduced it. Without it a consumer that cares about
/// settlement would have to call back into this service to ask, which is the
/// coupling the topic exists to avoid. The outstanding amount is deliberately
/// <em>not</em> carried: it is this service's own derived figure, and putting it
/// on the topic would invite another service to hold a copy that goes stale the
/// moment the next payment lands.
/// </para>
/// </remarks>
public sealed class PaymentReceivedPayload
{
    public required Guid PaymentId { get; init; }

    public required Guid InvoiceId { get; init; }

    /// <summary>
    /// The project the invoice was raised against. Carried because a consumer
    /// reasons about projects, not invoice ids, and it is also the event's Kafka
    /// key.
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>What was paid.</summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// The Client who recorded it, from the <c>sub</c> claim of their token.
    /// Carried so a consumer's own audit trail can name the person who caused the
    /// change it makes in reaction, rather than inventing an actor.
    /// </summary>
    public required Guid PaidBy { get; init; }

    /// <summary>
    /// The invoice's status after this payment — <c>Paid</c> when this payment
    /// settled it, <c>Pending</c> when something is still owed.
    /// </summary>
    public required InvoiceStatus InvoiceStatus { get; init; }

    public static PaymentReceivedPayload From(Payment payment, Guid projectId, InvoiceStatus invoiceStatus) => new()
    {
        PaymentId = payment.Id,
        InvoiceId = payment.InvoiceId,
        ProjectId = projectId,
        Amount = payment.Amount,
        PaidBy = payment.PaidBy,
        InvoiceStatus = invoiceStatus
    };
}
