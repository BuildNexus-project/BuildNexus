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

    /// <summary>
    /// Where the build phase stands, or <c>null</c> when construction has not been
    /// started (US-14 AC-4's "summary viewable by the Client").
    /// </summary>
    /// <remarks>
    /// Added to this response rather than given an endpoint of its own: the Client
    /// already has one screen that polls this, and the handover is the headline of the
    /// same story the milestones tell — "100% of milestones done" and "handed over on
    /// the 20th" belong in one answer, not two that can disagree.
    /// <para>
    /// <c>null</c> is the normal state for a project whose design is approved but whose
    /// build has not begun, not an error.
    /// </para>
    /// </remarks>
    public ConstructionPhaseSummary? Phase { get; set; }

    public static ProjectProgressSummaryResponse From(
        ProjectProgress progress,
        IReadOnlyList<Milestone> milestones,
        ConstructionPhase? phase) => new()
    {
        ProjectId = progress.ProjectId,
        TotalMilestones = progress.TotalMilestones,
        CompletedMilestones = progress.CompletedMilestones,
        ProgressPercent = progress.ProgressPercent,
        Milestones = milestones.Select(MilestoneResponse.From).ToList(),
        Phase = phase is null ? null : ConstructionPhaseSummary.From(phase)
    };
}

/// <summary>
/// The build phase as a Client sees it — when their project started, finished, and was
/// handed over to them (US-14 AC-4).
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="ConstructionPhaseResponse"/>, which the
/// Project Manager's own screen reads. The difference is
/// <c>handedOverByUserId</c>: that is a staff account id, of no use to a Client and not
/// theirs to see. A Client is told their project was handed over and when, not which
/// employee pressed the button.
/// </remarks>
public class ConstructionPhaseSummary
{
    /// <summary>
    /// Serialised as its name (<c>"Started"</c>, <c>"Completed"</c>,
    /// <c>"HandedOver"</c>) by the converter registered in <c>Program.cs</c>.
    /// </summary>
    public ConstructionPhaseStatus Status { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary><c>null</c> until the build is marked complete.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary><c>null</c> until the project is handed over.</summary>
    public DateTime? HandedOverAtUtc { get; set; }

    public static ConstructionPhaseSummary From(ConstructionPhase phase) => new()
    {
        Status = phase.Status,
        StartedAtUtc = phase.StartedAtUtc,
        CompletedAtUtc = phase.CompletedAtUtc,
        HandedOverAtUtc = phase.HandedOverAtUtc
    };
}
