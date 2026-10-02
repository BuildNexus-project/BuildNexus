namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// What a <c>DesignApproved</c> carries, as far as this service cares (US-26).
/// </summary>
/// <remarks>
/// A deliberate subset — the Design Service also sends the document and version ids and who
/// approved it, and none of them change who is told or what they are told. Reading only these
/// fields means one added on the publishing side cannot break this consumer.
/// </remarks>
public sealed class DesignApprovedPayload
{
    public Guid ProjectId { get; init; }

    /// <summary>The Architect's name for the document — "Ground floor plan".</summary>
    public string DocumentName { get; init; } = string.Empty;

    public int VersionNumber { get; init; }
}
