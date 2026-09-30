namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// One milestone on a running build that is not finished yet — a line on the
/// Project Manager's dashboard (US-21 AC-3).
/// </summary>
/// <remarks>
/// "Due" here means <em>outstanding</em>: milestones carry no due date (US-12
/// defines only <c>NotStarted</c>, <c>InProgress</c> and <c>Completed</c>), so there
/// is nothing to be late against. Adding one is a change to how milestones are
/// created, which belongs to that story, not to a read-only dashboard.
/// </remarks>
public class OutstandingMilestone
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The Project Manager's own label — "Foundation poured", "Roof on".</summary>
    public required string Name { get; init; }

    /// <summary>Either <see cref="MilestoneStatus.NotStarted"/> or <see cref="MilestoneStatus.InProgress"/>.</summary>
    public required MilestoneStatus Status { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
