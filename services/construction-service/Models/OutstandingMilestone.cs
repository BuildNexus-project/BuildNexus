namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// One milestone on a running build that is not finished yet — a line on the
/// Project Manager's dashboard (US-21 AC-3).
/// </summary>
/// <remarks>
/// "Due" means <em>outstanding</em>: every milestone not yet <see cref="MilestoneStatus.Completed"/>
/// on a build that has started. Where the Project Manager has also given the milestone a
/// <see cref="DueDate"/>, it can additionally be late; one without a date never is, and is
/// listed after the dated ones.
/// </remarks>
public class OutstandingMilestone
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The Project Manager's own label — "Foundation poured", "Roof on".</summary>
    public required string Name { get; init; }

    /// <summary>Either <see cref="MilestoneStatus.NotStarted"/> or <see cref="MilestoneStatus.InProgress"/>.</summary>
    public required MilestoneStatus Status { get; init; }

    /// <summary>The day it should be finished by, or <c>null</c> when the Project Manager has not said.</summary>
    public DateOnly? DueDate { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
