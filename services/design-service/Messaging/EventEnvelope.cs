using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// The envelope every BuildNexus event is wrapped in, whichever service
/// published it:
/// <code>
/// { "eventType": "...", "eventId": "&lt;guid&gt;", "occurredAt": "&lt;ISO8601&gt;", "payload": { ... } }
/// </code>
/// </summary>
/// <remarks>
/// One shape across every publishing service, so a consumer can read the type
/// and decide whether it cares before it looks at the payload at all. The same
/// shape as <c>project-service</c>'s own envelope, by agreement — see its copy
/// for the full reasoning.
/// </remarks>
public sealed class EventEnvelope<TPayload>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>What happened — for example <c>DesignApproved</c>.</summary>
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
