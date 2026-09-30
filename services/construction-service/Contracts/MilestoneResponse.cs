using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// One row in a milestone listing, or the body of a create/patch reply
/// (US-12). A near-mirror of <see cref="Milestone"/>, kept separate so the
/// wire contract does not move if the model gains a field this response
/// should not expose.
/// </summary>
public class MilestoneResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Serialised as its name (<c>"NotStarted"</c>, <c>"InProgress"</c>,
    /// <c>"Completed"</c>) via the <c>JsonStringEnumConverter</c> registered
    /// in <c>Program.cs</c> — kinder to the React side than an integer.
    /// </summary>
    public MilestoneStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public static MilestoneResponse From(Milestone milestone) => new()
    {
        Id = milestone.Id,
        ProjectId = milestone.ProjectId,
        Name = milestone.Name,
        Status = milestone.Status,
        CreatedAtUtc = milestone.CreatedAtUtc,
        UpdatedAtUtc = milestone.UpdatedAtUtc
    };
}

/// <summary>
/// A project's aggregated milestone progress (US-12 AC-3), for
/// <c>GET .../progress</c>. The percentage is computed from the milestones on
/// every read — no column stores it, so the response cannot disagree with the
/// underlying rows.
/// </summary>
public class ProjectProgressResponse
{
    public Guid ProjectId { get; set; }

    public int TotalMilestones { get; set; }

    public int CompletedMilestones { get; set; }

    public decimal ProgressPercent { get; set; }

    public static ProjectProgressResponse From(ProjectProgress progress) => new()
    {
        ProjectId = progress.ProjectId,
        TotalMilestones = progress.TotalMilestones,
        CompletedMilestones = progress.CompletedMilestones,
        ProgressPercent = progress.ProgressPercent
    };
}