using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The envelope every BuildNexus event is wrapped in, whichever service
/// published it:
/// <code>
/// { "eventType": "...", "eventId": "&lt;guid&gt;", "occurredAt": "&lt;ISO8601&gt;", "payload": { ... } }
/// </code>
/// </summary>
/// <remarks>
/// One shape across all four publishing services, so a consumer can read the
/// type and decide whether it cares before it looks at the payload at all.
/// <para>
/// <see cref="EventId"/> is per-event, not per-project: it identifies this
/// publication, which is what lets a consumer recognise a message it has
/// already handled if the broker redelivers it.
/// </para>
/// </remarks>
public sealed class EventEnvelope<TPayload>
{
    /// <summary>
    /// Matches the property names in the agreed envelope exactly. Payload
    /// properties go out camel-cased for the same reason the REST responses do.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // An absent optional field is carried as null rather than dropped, so a
        // consumer reads the same set of keys on every message.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>What happened — for example <c>ProjectCreated</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>Identifies this publication, so a redelivery can be recognised.</summary>
    public required Guid EventId { get; init; }

    /// <summary>When it happened, in UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    public required TPayload Payload { get; init; }

    /// <summary>
    /// Wraps a payload, minting the event id and stamping the time.
    /// </summary>
    public static EventEnvelope<TPayload> Create(string eventType, TPayload payload, DateTimeOffset occurredAt) =>
        new()
        {
            EventType = eventType,
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Payload = payload
        };

    /// <summary>
    /// The JSON that goes on the wire. <see cref="DateTimeOffset"/> serialises
    /// as ISO 8601 by default, which is the format the envelope calls for.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}
