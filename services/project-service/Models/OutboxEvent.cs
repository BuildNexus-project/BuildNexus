namespace BuildNexus.ProjectService.Models;

/// <summary>
/// One event waiting to reach — or already delivered to — the message bus,
/// mapped by hand from the <c>project_outbox_events</c> table.
/// </summary>
/// <remarks>
/// The row is written in the same transaction as the state change it describes,
/// which is what makes a publish reliable: the event and the change it announces
/// commit together or not at all, so there is no window in which a project moved
/// and nobody was told. Getting it onto Kafka afterwards is the dispatcher's
/// job, and a broker that is down delays that rather than losing it.
/// <para>
/// <see cref="Envelope"/> is serialised once, when the change happens, and sent
/// byte for byte however many attempts it takes. Re-serialising at dispatch
/// would mint a new <c>eventId</c> and a later <c>occurredAt</c> on every retry,
/// and a consumer deduplicating on the id would see each one as a new event.
/// </para>
/// </remarks>
public class OutboxEvent
{
    /// <summary>
    /// Assigned by the database on insert, and the order events are dispatched
    /// in. Zero on an event that has not been written yet.
    /// </summary>
    /// <remarks>
    /// AUTO_INCREMENT rather than <see cref="OccurredAt"/>: that column holds
    /// whole seconds, and two events raised in one transaction share its value
    /// exactly. This is monotonic whatever the clock's resolution — the same
    /// reason the status history stopped ordering on its timestamp.
    /// </remarks>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// The envelope's own <c>eventId</c>, held as a column too so a message on
    /// the topic can be traced back to the row that produced it.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The project the event is about, and the Kafka message key — every event
    /// about one project lands on the same partition and so reaches consumers
    /// in the order it happened.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>One of <see cref="Messaging.ProjectEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The complete envelope JSON, exactly as it will go on the wire.</summary>
    public string Envelope { get; set; } = string.Empty;

    /// <summary>When the change this announces actually happened, in UTC.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// When the broker acknowledged it, or <c>null</c> while it is still
    /// waiting. The one field that says whether this row's work is done.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// How many times dispatch has been attempted. Rises on every failure and
    /// on the attempt that finally succeeds.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Why the last attempt failed, or <c>null</c> if none has. Kept after a
    /// later attempt succeeds is pointless, so it is cleared on delivery.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>Whether the broker has taken it.</summary>
    public bool IsPublished => PublishedAt.HasValue;
}
