using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// Mints the events this service raises, ready to be enqueued on the outbox.
/// </summary>
/// <remarks>
/// One method per event US-16 names, and no general "raise anything" call: the
/// set of events on <c>payment-events</c> is a contract with the other services,
/// and it should not be possible to add to it by accident.
/// <para>
/// The envelope is serialised here, at the moment the payment is recorded, and
/// the resulting JSON is what the dispatcher later sends — byte for byte, however
/// many attempts it takes. Serialising at dispatch instead would mint a fresh
/// <c>eventId</c> and a later <c>occurredAt</c> on every retry, and a consumer
/// deduplicating on the id would treat each one as a new event.
/// </para>
/// <para>
/// Nothing here talks to Kafka, so which events a payment raises can be read and
/// tested without a broker.
/// </para>
/// </remarks>
public static class PaymentEvents
{
    /// <summary>A Client has recorded a payment against an invoice (AC-3).</summary>
    public static OutboxEvent Received(Payment payment, Guid projectId, InvoiceStatus invoiceStatus)
    {
        var envelope = EventEnvelope<PaymentReceivedPayload>.Create(
            PaymentEventTypes.PaymentReceived,
            PaymentReceivedPayload.From(payment, projectId, invoiceStatus),
            // The moment the payment was recorded, not "now" — they are the same
            // instant here, and taking it off the payment keeps the event's
            // occurredAt and the row's recorded_at from ever disagreeing.
            EventTimestamp.AsUtc(payment.RecordedAtUtc));

        return new OutboxEvent
        {
            // The row and the envelope share an id, so a message on the topic can
            // be traced back to the row that produced it.
            Id = envelope.EventId,
            ProjectId = projectId,
            InvoiceId = payment.InvoiceId,
            EventType = PaymentEventTypes.PaymentReceived,
            Envelope = envelope.ToJson(),
            OccurredAt = payment.RecordedAtUtc
        };
    }
}
