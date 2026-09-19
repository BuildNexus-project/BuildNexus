using System.Text.Json.Serialization;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// The fields this service reads from a <c>DesignApproved</c> event's payload —
/// the project whose design was signed off, the document it was, and when.
/// </summary>
/// <remarks>
/// A narrower view of <c>design-service</c>'s own <c>DesignApprovedPayload</c>:
/// that carries the version and the approver too, which construction prep does
/// not need.
/// </remarks>
public sealed class DesignApprovedPayload
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; init; }

    [JsonPropertyName("documentId")]
    public Guid DocumentId { get; init; }

    [JsonPropertyName("approvedAt")]
    public DateTimeOffset ApprovedAt { get; init; }
}
