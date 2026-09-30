namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The outstanding milestones across every running build: how many there are in
/// all, and the first few of them.
/// </summary>
/// <remarks>
/// The total and the list are read together because they answer different
/// questions — "how much is left" and "what is next" — and the list is capped so
/// a large portfolio does not send every milestone to draw a dashboard tile. The
/// total is never capped.
/// </remarks>
public class OutstandingMilestones
{
    /// <summary>Every outstanding milestone on a running build, whatever the cap on <see cref="Items"/>.</summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// At most the cap asked for. Those already <see cref="MilestoneStatus.InProgress"/>
    /// first, then those not started; each group in the order the milestones were planned.
    /// </summary>
    public required IReadOnlyList<OutstandingMilestone> Items { get; init; }
}
