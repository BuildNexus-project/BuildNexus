using System.Text.Json.Serialization;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// The fields this service reads from a <c>ConstructionCompleted</c> event's
/// payload — the project whose build finished, and when.
/// </summary>
/// <remarks>
/// A narrower view of <c>construction-service</c>'s own payload, which also
/// carries the start time, the milestone count and who completed it. None of
/// those bears on settlement, so reading only these two keeps the coupling to
/// the fields this consumer actually depends on.
/// </remarks>
public sealed class ConstructionCompletedPayload
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; init; }

    /// <summary>When the build was completed, from the event's own payload.</summary>
    [JsonPropertyName("completedAt")]
    public DateTimeOffset CompletedAt { get; init; }
}
