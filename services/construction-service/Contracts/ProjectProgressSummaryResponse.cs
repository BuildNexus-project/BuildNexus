using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// Everything a Client's dashboard needs to show how their project is
/// advancing (US-13 AC-1): the per-milestone status, and the overall rollup
/// the progress bar is drawn from.
/// </summary>
/// <remarks>
/// The rollup fields mirror <see cref="ProjectProgressResponse"/> rather than
/// nesting it, so the dashboard reads <c>progressPercent</c> off the top level
/// the same way the PM's view does.
/// <para>
/// One response rather than the two the PM's screen makes (<c>.../milestones</c>
/// plus <c>.../progress</c>): the dashboard needs both together or neither, and
/// AC-2 has it re-reading on a timer. Two requests per poll would double the
/// traffic and open a window where the list and the percentage disagree because
/// a status changed between them.
/// </para>
/// </remarks>
public class ProjectProgressSummaryResponse
{
    public Guid ProjectId { get; set; }

    public int TotalMilestones { get; set; }

    public int CompletedMilestones { get; set; }

    /// <summary>
    /// <c>CompletedMilestones / TotalMilestones × 100</c>, recomputed by the
    /// service on every read. Zero when the project has no milestones yet — a
    /// real state for a project whose design was only just approved, not a
    /// failure.
    /// </summary>
    public decimal ProgressPercent { get; set; }

    /// <summary>
    /// The milestones, oldest first — the order the Project Manager planned
    /// them in, which is the order the build runs in. Empty when none have been
    /// defined yet.
    /// </summary>
    public IReadOnlyList<MilestoneResponse> Milestones { get; set; } = [];

    public static ProjectProgressSummaryResponse From(
        ProjectProgress progress,
        IReadOnlyList<Milestone> milestones) => new()
    {
        ProjectId = progress.ProjectId,
        TotalMilestones = progress.TotalMilestones,
        CompletedMilestones = progress.CompletedMilestones,
        ProgressPercent = progress.ProgressPercent,
        Milestones = milestones.Select(MilestoneResponse.From).ToList()
    };
}
