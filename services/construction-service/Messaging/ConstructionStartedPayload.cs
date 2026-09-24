using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// What a <c>ConstructionStarted</c> event carries (AC-1).
/// </summary>
/// <remarks>
/// Deliberately small. The Project Service's only job on receiving this is to move
/// the project to <c>Construction</c>, and it needs the project id and the time to
/// do it. <see cref="MilestoneCount"/> is included because it is the fact that
/// made the transition legal — a consumer or a human tracing the topic can see the
/// build started against a real plan without querying this service.
/// <para>
/// No milestone names or statuses: those are this service's own state, and putting
/// them on the topic would invite another service to hold a stale copy of
/// something it can read from US-13's endpoint when it needs it.
/// </para>
/// </remarks>
public sealed class ConstructionStartedPayload
{
    public required Guid ProjectId { get; init; }

    /// <summary>When the Project Manager started the build.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>How many milestones the project had defined when it started.</summary>
    public required int MilestoneCount { get; init; }

    public static ConstructionStartedPayload From(ConstructionPhase phase, int milestoneCount) => new()
    {
        ProjectId = phase.ProjectId,
        StartedAt = EventTimestamp.AsUtc(phase.StartedAtUtc),
        MilestoneCount = milestoneCount
    };
}
