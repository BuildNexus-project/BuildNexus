namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Every project grouped by its current status (US-18): the pipeline at a
/// glance.
/// </summary>
public class ProjectStatusReport
{
    /// <summary>One group per reported status, in lifecycle order.</summary>
    public IReadOnlyList<ProjectStatusGroup> Groups { get; init; } = [];

    public int TotalProjects => Groups.Sum(group => group.Count);

    public decimal TotalBudget => Groups.Sum(group => group.TotalBudget);
}
