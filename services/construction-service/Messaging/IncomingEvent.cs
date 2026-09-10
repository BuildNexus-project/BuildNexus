using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// The envelope every BuildNexus event arrives in, read from the consume side:
/// <code>
/// { "eventType": "...", "eventId": "&lt;guid&gt;", "occurredAt": "&lt;ISO8601&gt;", "payload": { ... } }
/// </code>
/// </summary>
/// <remarks>
/// The same shape the publishing services serialise — see
/// <c>design-service</c>'s <c>EventEnvelope&lt;TPayload&gt;</c>. The payload is
/// kept as a raw <see cref="JsonElement"/> so <see cref="EventType"/> can be
/// checked before deciding whether, and how, to read it.
/// </remarks>
public sealed class IncomingEvent
{
    /// <summary>The published bytes are camelCase; read them either case, to be safe against a producer tweak.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public Guid EventId { get; init; }

    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}
