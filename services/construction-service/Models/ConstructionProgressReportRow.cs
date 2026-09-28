namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// One project's line in the construction progress report (US-19 AC-1): how far its
/// build has got overall, and how its milestones break down by status.
/// </summary>
/// <remarks>
/// Every field is derived from the <c>construction_milestones</c> rows on the fly, the
/// same way <see cref="ProjectProgress"/> is — no column stores
/// <see cref="ProgressPercent"/>, so a report cannot show a stale total that disagrees
/// with the milestones behind it.
/// <para>
/// A report row, not a resource: nothing writes one, and the shape exists only to carry
/// an aggregate query's output to the response.
/// </para>
/// </remarks>
public class ConstructionProgressReportRow
{
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// Where the build phase stands, or <c>null</c> when construction has not been
    /// started — a project whose milestones are planned but whose build has not begun
    /// is a real line in this report, not an omission.
    /// </summary>
    public ConstructionPhaseStatus? PhaseStatus { get; init; }

    /// <summary>How many milestones the project has, in any state.</summary>
    public required int TotalMilestones { get; init; }

    public required int CompletedMilestones { get; init; }

    public required int InProgressMilestones { get; init; }

    public required int NotStartedMilestones { get; init; }

    /// <summary>
    /// <c>CompletedMilestones / TotalMilestones × 100</c>, rounded to two decimals —
    /// the same calculation the per-project progress read uses, so the report and the
    /// project's own screen cannot disagree.
    /// </summary>
    public required decimal ProgressPercent { get; init; }
}
