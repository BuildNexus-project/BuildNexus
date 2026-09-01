using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Mints the events this service raises, ready to be enqueued on the outbox.
/// </summary>
/// <remarks>
/// One method per event US-22 names, and no general "raise anything" call: the
/// set of events on <c>project-events</c> is a contract with four other
/// services, and it should not be possible to add to it by accident.
/// <para>
/// The envelope is serialised here, at the moment of the state change, and the
/// resulting JSON is what the dispatcher later sends — byte for byte, however
/// many attempts it takes. Serialising at dispatch instead would mint a fresh
/// <c>eventId</c> and a later <c>occurredAt</c> on every retry, and a consumer
/// deduplicating on the id would treat each one as a new event.
/// </para>
/// <para>
/// Nothing here talks to Kafka, so which events a state change raises can be
/// read and tested without a broker.
/// </para>
/// </remarks>
public static class ProjectEvents
{
    /// <summary>A Client has submitted a new project.</summary>
    public static OutboxEvent Created(Project project) =>
        Raise(
            ProjectEventTypes.ProjectCreated,
            project.Id,
            ProjectCreatedPayload.From(project),
            project.CreatedAt);

    /// <summary>A project has moved to a new status.</summary>
    public static OutboxEvent Updated(Project project, ProjectStatusChange change) =>
        Raise(
            ProjectEventTypes.ProjectUpdated,
            project.Id,
            ProjectUpdatedPayload.From(project, change),
            change.ChangedAt);

    /// <summary>A project's design has been signed off.</summary>
    public static OutboxEvent Approved(Project project, ProjectStatusChange change) =>
        Raise(
            ProjectEventTypes.ProjectApproved,
            project.Id,
            ProjectApprovedPayload.From(project, change),
            change.ChangedAt);

    /// <summary>
    /// Everything one status change announces, in the order it goes on the
    /// topic.
    /// </summary>
    /// <remarks>
    /// Every transition is a <see cref="ProjectEventTypes.ProjectUpdated"/>.
    /// The move that lands on <see cref="ProjectStatus.DesignApproved"/> is also
    /// a <see cref="ProjectEventTypes.ProjectApproved"/>, and it follows rather
    /// than replaces the update — a consumer reading both would otherwise see
    /// the project reach a status it was never told about.
    /// <para>
    /// Which transition counts as the approval is decided here, once, rather
    /// than at the call site: it is the single place that has to change if the
    /// lifecycle ever gains a stage.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<OutboxEvent> ForStatusChange(Project project, ProjectStatusChange change) =>
        change.ToStatus == ProjectStatus.DesignApproved
            ? [Updated(project, change), Approved(project, change)]
            : [Updated(project, change)];

    /// <summary>
    /// Wraps a payload in the agreed envelope and parcels it up as an outbox
    /// row, with the id the envelope was minted with.
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
            // The row and the envelope share an id, so a message on the topic
            // can be traced back to the row that produced it.
            Id = envelope.EventId,
            ProjectId = projectId,
            EventType = eventType,
            Envelope = envelope.ToJson(),
            OccurredAt = occurredAtUtc
        };
    }
}
