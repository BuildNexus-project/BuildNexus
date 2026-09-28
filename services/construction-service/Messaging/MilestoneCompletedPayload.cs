using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// What a <c>MilestoneCompleted</c> event carries (US-24): which milestone finished, on
/// which project, and when.
/// </summary>
/// <remarks>
/// The milestone's name travels with it, not just its id. A consumer reacting to build
/// progress — or a person reading the topic to work out what happened — has no way to
/// resolve a milestone id against this service's database, and should not be invited to
/// try. The name is what makes the event legible on its own.
/// <para>
/// No progress percentage or milestone tally here. Those are derived facts that change as
/// other milestones move, and an event carrying a snapshot of them would be read as
/// current long after it stopped being so. A consumer that needs the rollup reads it from
/// the progress endpoint, which recomputes it.
/// </para>
/// </remarks>
public sealed class MilestoneCompletedPayload
{
    public required Guid MilestoneId { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The Project Manager's own label for the milestone — "Foundation", "Roof on".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the milestone was marked complete, taken from the row's <c>updated_at</c> rather
    /// than the clock, so the event's <c>occurredAt</c> and the stored stamp cannot disagree.
    /// </summary>
    public required DateTimeOffset CompletedAt { get; init; }

    public static MilestoneCompletedPayload From(Milestone milestone) => new()
    {
        MilestoneId = milestone.Id,
        ProjectId = milestone.ProjectId,
        Name = milestone.Name,
        CompletedAt = EventTimestamp.AsUtc(milestone.UpdatedAtUtc)
    };
}
