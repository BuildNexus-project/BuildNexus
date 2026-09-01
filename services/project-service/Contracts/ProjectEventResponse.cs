using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// One event this service raised for a project, and how its delivery went.
/// </summary>
/// <remarks>
/// What an administrator needs to answer "was this actually announced?" —
/// which, now that a publish no longer happens inside the request, is a
/// question nothing else on the API can answer.
/// <para>
/// The envelope itself is deliberately not here. It repeats what the project
/// endpoints already return, and a list of JSON blobs is a poor way to read a
/// delivery log; what this view is for is whether each event got out, and if
/// not, why not.
/// </para>
/// </remarks>
public class ProjectEventResponse
{
    /// <summary>
    /// The event's own id — the same <c>eventId</c> carried in the envelope on
    /// the topic, so a message a consumer is asking about can be matched to
    /// this row.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// <c>ProjectCreated</c>, <c>ProjectUpdated</c> or <c>ProjectApproved</c>.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>When the change it announces happened — not when it was sent.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// When the broker acknowledged it, or <c>null</c> while it is still
    /// waiting. The one field that says whether the event got out.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// How many times delivery has been attempted. More than one on a delivered
    /// event means it got there eventually, which is the outbox working rather
    /// than a fault.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Why the last attempt failed, or <c>null</c>. Cleared once the event is
    /// delivered, so a value here alongside a null
    /// <see cref="PublishedAt"/> is the thing worth looking at.
    /// </summary>
    public string? LastError { get; set; }

    public static ProjectEventResponse From(OutboxEvent outboxEvent) => new()
    {
        Id = outboxEvent.Id,
        EventType = outboxEvent.EventType,
        OccurredAt = outboxEvent.OccurredAt,
        PublishedAt = outboxEvent.PublishedAt,
        AttemptCount = outboxEvent.AttemptCount,
        LastError = outboxEvent.LastError
    };
}
