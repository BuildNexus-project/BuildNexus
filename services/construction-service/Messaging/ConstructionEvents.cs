using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// Mints the events this service raises, ready to be enqueued on the outbox.
/// </summary>
/// <remarks>
/// One method per event US-14 names, and no general "raise anything" call: the set
/// of events on <c>construction-events</c> is a contract with the other services,
/// and it should not be possible to add to it by accident.
/// <para>
/// The envelope is serialised here, at the moment of the transition, and the
/// resulting JSON is what the dispatcher later sends — byte for byte, however many
/// attempts it takes. Serialising at dispatch instead would mint a fresh
/// <c>eventId</c> and a later <c>occurredAt</c> on every retry, and a consumer
/// deduplicating on the id would treat each one as a new event.
/// </para>
/// <para>
/// Nothing here talks to Kafka, so which events a transition raises can be read
/// and tested without a broker.
/// </para>
/// </remarks>
public static class ConstructionEvents
{
    /// <summary>A Project Manager has formally started the build (AC-1).</summary>
    public static OutboxEvent Started(ConstructionPhase phase, int milestoneCount) =>
        Raise(
            ConstructionEventTypes.ConstructionStarted,
            phase.ProjectId,
            ConstructionStartedPayload.From(phase, milestoneCount),
            phase.StartedAtUtc);

    /// <summary>Every milestone is done and the build is marked complete (AC-2).</summary>
    public static OutboxEvent Completed(ConstructionPhase phase, int milestoneCount) =>
        Raise(
            ConstructionEventTypes.ConstructionCompleted,
            phase.ProjectId,
            ConstructionCompletedPayload.From(phase, milestoneCount),
            // The moment the transition happened, not "now" — they are the same
            // instant here, and taking it off the phase keeps the event's
            // occurredAt and the row's completed_at from ever disagreeing.
            phase.CompletedAtUtc!.Value);

    /// <summary>
    /// Wraps a payload in the agreed envelope and parcels it up as an outbox row,
    /// with the id the envelope was minted with.
    /// </summary>
    private static OutboxEvent Raise<TPayload>(
        string eventType,
        Guid projectId,
        TPayload payload,
        DateTime occurredAtUtc)
    {
        var envelope = EventEnvelope<TPayload>.Create(eventType, payload, EventTimestamp.AsUtc(occurredAtUtc));

        return new OutboxEvent
        {
            // The row and the envelope share an id, so a message on the topic can
            // be traced back to the row that produced it.
            Id = envelope.EventId,
            ProjectId = projectId,
            EventType = eventType,
            Envelope = envelope.ToJson(),
            OccurredAt = occurredAtUtc
        };
    }
}
