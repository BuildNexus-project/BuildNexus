using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// What a <c>ConstructionCompleted</c> event carries (AC-2).
/// </summary>
/// <remarks>
/// <see cref="StartedAt"/> rides along with <see cref="CompletedAt"/> so a
/// consumer can see the span of the build from this one message, without having
/// kept the <c>ConstructionStarted</c> it may never have received — a consumer
/// that came online mid-build would otherwise know the end and not the beginning.
/// <see cref="MilestoneCount"/> is the tally that satisfied AC-2's "every
/// milestone Completed": every one of them was done, so the count is also the
/// number completed.
/// </remarks>
public sealed class ConstructionCompletedPayload
{
    public required Guid ProjectId { get; init; }

    /// <summary>When the build started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the Project Manager marked it complete.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>How many milestones the project had — all of them Completed, by AC-2.</summary>
    public required int MilestoneCount { get; init; }

    /// <summary>
    /// The Project Manager who marked the build complete, from the <c>sub</c> claim
    /// of their token. Carried for the same reason
    /// <see cref="ConstructionStartedPayload.StartedBy"/> is.
    /// </summary>
    public required Guid CompletedBy { get; init; }

    public static ConstructionCompletedPayload From(
        ConstructionPhase phase,
        int milestoneCount,
        Guid completedBy) => new()
    {
        ProjectId = phase.ProjectId,
        StartedAt = EventTimestamp.AsUtc(phase.StartedAtUtc),
        // CompletedAtUtc is non-null on a phase this event is raised for: the
        // transition that raises it is the one that sets the column.
        CompletedAt = EventTimestamp.AsUtc(phase.CompletedAtUtc!.Value),
        MilestoneCount = milestoneCount,
        CompletedBy = completedBy
    };
}
