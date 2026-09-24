namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The <c>eventType</c> values this service reads off <c>construction-events</c>.
/// </summary>
/// <remarks>
/// Only the two US-14 publishes. Anything else on that topic is another service's
/// business and is skipped — a consumer that reacted to types nobody agreed it
/// would read is a coupling the publisher does not know it has.
/// </remarks>
public static class ConstructionEventTypes
{
    /// <summary>A Project Manager has started the build: this project moves to Construction.</summary>
    public const string ConstructionStarted = nameof(ConstructionStarted);

    /// <summary>Every milestone is done and the build is complete: this project moves to Completed.</summary>
    public const string ConstructionCompleted = nameof(ConstructionCompleted);
}

/// <summary>
/// What a <c>ConstructionStarted</c> carries, as far as this service cares.
/// </summary>
/// <remarks>
/// A deliberate subset — the Construction Service also sends the milestone count
/// and the start time, and neither changes what this service does. Reading only the
/// fields that matter means a field added on the publishing side cannot break this
/// consumer.
/// </remarks>
public sealed class ConstructionStartedPayload
{
    public Guid ProjectId { get; init; }

    /// <summary>
    /// The Project Manager who started the build. Recorded as the author of the
    /// status change this event causes, so the history says who decided rather than
    /// attributing the move to nobody.
    /// </summary>
    public Guid StartedBy { get; init; }
}

/// <summary>What a <c>ConstructionCompleted</c> carries, as far as this service cares.</summary>
public sealed class ConstructionCompletedPayload
{
    public Guid ProjectId { get; init; }

    /// <summary>The Project Manager who marked the build complete.</summary>
    public Guid CompletedBy { get; init; }
}
