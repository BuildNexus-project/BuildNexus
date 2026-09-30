namespace BuildNexus.ProjectService.Models;

/// <summary>
/// The projects of one status in the report, with the figures an Admin reads
/// off a pipeline: how many, and how much money is riding on them.
/// </summary>
public class ProjectStatusGroup
{
    public ProjectStatus Status { get; init; }

    /// <summary>Newest first. Empty when nothing is in this status — the group is still reported.</summary>
    public IReadOnlyList<ProjectReportRow> Projects { get; init; } = [];

    public int Count => Projects.Count;

    /// <summary>The Clients' combined budgets for these projects.</summary>
    public decimal TotalBudget => Projects.Sum(project => project.Budget);
}
