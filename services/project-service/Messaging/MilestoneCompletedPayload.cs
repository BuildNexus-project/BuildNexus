namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// What a <c>MilestoneCompleted</c> carries, as far as this service cares (US-26).
/// </summary>
/// <remarks>
/// A deliberate subset — the Construction Service also sends the milestone id and when it was
/// completed. The id means nothing outside that service, and the time arrives on the envelope.
/// </remarks>
public sealed class MilestoneCompletedPayload
{
    public Guid ProjectId { get; init; }

    /// <summary>The Project Manager's own label for the milestone — "Foundation", "Roof on".</summary>
    public string Name { get; init; } = string.Empty;
}
