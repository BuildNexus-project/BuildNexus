namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The three states a milestone moves through, exactly as US-12's Acceptance
/// Criteria name them. Kept as an enum for the C# side; persisted as its own
/// name (<c>"NotStarted"</c>, <c>"InProgress"</c>, <c>"Completed"</c>) rather
/// than an integer, so a DBA reading the table can see what a row means.
/// </summary>
public enum MilestoneStatus
{
    NotStarted,
    InProgress,
    Completed
}

/// <summary>
/// One construction milestone a Project Manager defined for an approved
/// project (US-12). Owned entirely by the Construction Service — no other
/// service sees or joins against these rows.
/// </summary>
public class Milestone
{
    public required Guid Id { get; init; }

    /// <summary>
    /// The project this milestone belongs to. Not a foreign key across a
    /// service boundary — copied off the <c>milestone_setups</c> row the
    /// <c>DesignApproved</c> consumer planted for the project.
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// The PM's own label ("Foundation poured", "Roof on"). Unique inside a
    /// project — two projects may reuse the same name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Where this milestone stands. A fresh milestone is <see cref="MilestoneStatus.NotStarted"/>;
    /// the PM moves it along through <c>PATCH .../status</c>.
    /// </summary>
    public required MilestoneStatus Status { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>Stamped on every status change, so a caller can see when it last moved.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>
/// A project's aggregated milestone progress (US-12 AC-3). Every field is
/// derived from the <c>construction_milestones</c> rows on the fly — no column
/// stores <see cref="ProgressPercent"/>, so a stale copy cannot drift from
/// what the milestones actually say.
/// </summary>
public class ProjectProgress
{
    public required Guid ProjectId { get; init; }

    /// <summary>How many milestones the project has, in any state.</summary>
    public required int TotalMilestones { get; init; }

    public required int CompletedMilestones { get; init; }

    /// <summary>
    /// <c>CompletedMilestones / TotalMilestones × 100</c>, rounded to two
    /// decimal places. Zero when the project has no milestones yet — the
    /// project is approved but nothing has been defined, which is a real
    /// state, not an error.
    /// </summary>
    public required decimal ProgressPercent { get; init; }
}