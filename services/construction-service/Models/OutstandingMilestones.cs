namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The outstanding milestones across a set of running builds: how many there are in all, how
/// many of those are late, and the first few of them.
/// </summary>
/// <remarks>
/// The totals and the list are read together because they answer different questions — "how
/// much is left", "how much of it is late" and "what is next" — and the list is capped so a
/// large portfolio does not send every milestone to draw a dashboard tile. The totals are
/// never capped.
/// </remarks>
public class OutstandingMilestones
{
    /// <summary>Every outstanding milestone on a running build, whatever the cap on <see cref="Items"/>.</summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// How many of <see cref="TotalCount"/> have a due date before the day the query was made
    /// for. A milestone with no date is never counted here — it cannot be late.
    /// </summary>
    public required int OverdueCount { get; init; }

    /// <summary>
    /// At most the cap asked for. Those with a due date first, soonest (and so most overdue)
    /// first; then those without one. Within a date, those already
    /// <see cref="MilestoneStatus.InProgress"/> before those not started, each in the order
    /// the milestones were planned.
    /// </summary>
    public required IReadOnlyList<OutstandingMilestone> Items { get; init; }
}
