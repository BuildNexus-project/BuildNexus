using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// Mints the events this service raises, ready to be enqueued on the outbox.
/// </summary>
/// <remarks>
/// One method per event US-11 names, and no general "raise anything" call: the
/// set of events on <c>design-events</c> is a contract with whichever services
/// consume it, and it should not be possible to add to it by accident.
/// <para>
/// The envelope is serialised here, at the moment of the state change, and the
/// resulting JSON is what the dispatcher later sends — byte for byte, however
/// many attempts it takes. Nothing here talks to Kafka, so which events a
/// state change raises can be read and tested without a broker.
/// </para>
/// </remarks>
public static class DesignEvents
{
    /// <summary>An Architect has uploaded a design document version.</summary>
    public static OutboxEvent Submitted(DesignDocument document, DesignDocumentVersion version) =>
        Raise(
            DesignEventTypes.DesignSubmitted,
            document.Id,
            DesignSubmittedPayload.From(document, version),
            version.UploadedAt);

    /// <summary>A Client has asked for changes on a design document version.</summary>
    public static OutboxEvent RevisionRequested(
        DesignVersionForReview version, Guid requestedBy, string comment, DateTime requestedAtUtc) =>
        Raise(
            DesignEventTypes.DesignRevisionRequested,
            version.DocumentId,
            DesignRevisionRequestedPayload.From(version, requestedBy, comment, requestedAtUtc),
            requestedAtUtc);

    /// <summary>A Client has approved a design document version.</summary>
    public static OutboxEvent Approved(DesignVersionForReview version, Guid approvedBy, DateTime approvedAtUtc) =>
        Raise(
            DesignEventTypes.DesignApproved,
            version.DocumentId,
            DesignApprovedPayload.From(version, approvedBy, approvedAtUtc),
            approvedAtUtc);

    /// <summary>
    /// Wraps a payload in the agreed envelope and parcels it up as an outbox
    /// row, with the id the envelope was minted with.
    /// </summary>
    private static OutboxEvent Raise<TPayload>(
        string eventType,
        Guid documentId,
        TPayload payload,
        DateTime occurredAtUtc)
    {
        var envelope = EventEnvelope<TPayload>.Create(eventType, payload, EventTimestamp.AsUtc(occurredAtUtc));

        return new OutboxEvent
        {
            // The row and the envelope share an id, so a message on the topic
            // can be traced back to the row that produced it.
            Id = envelope.EventId,
            DocumentId = documentId,
            EventType = eventType,
            Envelope = envelope.ToJson(),
            OccurredAt = occurredAtUtc
        };
    }
}
