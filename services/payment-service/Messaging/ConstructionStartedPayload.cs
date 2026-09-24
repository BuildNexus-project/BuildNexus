using System.Text.Json.Serialization;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// The fields this service reads from a <c>ConstructionStarted</c> event's
/// payload — the project whose build began, and the Project Manager who began
/// it.
/// </summary>
/// <remarks>
/// A narrower view of <c>construction-service</c>'s own
/// <c>ConstructionStartedPayload</c>, which also carries <c>startedAt</c> and
/// <c>milestoneCount</c>. Neither bears on what to bill, so reading only these
/// two keeps the coupling to the fields this consumer actually depends on — a
/// change to either of the others cannot break it.
/// </remarks>
public sealed class ConstructionStartedPayload
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; init; }

    /// <summary>
    /// The Project Manager who started the build, from the <c>sub</c> claim of
    /// their token. Recorded as the invoice's author: nobody typed this invoice,
    /// but a real person's decision caused it, and a bill with no answerable
    /// author is worse than one attributed to the person who triggered it.
    /// </summary>
    [JsonPropertyName("startedBy")]
    public Guid StartedBy { get; init; }
}
