using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// The projects of one status in the report, with the count and budget total an
/// Admin reads the pipeline by.
/// </summary>
public class ProjectStatusGroupResponse
{
    public string Status { get; set; } = string.Empty;

    public int Count { get; set; }

    public decimal TotalBudget { get; set; }

    /// <summary>Newest submitted first. Empty when nothing is in this status.</summary>
    public IReadOnlyList<ProjectReportRowResponse> Projects { get; set; } = [];

    public static ProjectStatusGroupResponse From(ProjectStatusGroup group) => new()
    {
        Status = group.Status.ToString(),
        Count = group.Count,
        TotalBudget = group.TotalBudget,
        Projects = group.Projects.Select(ProjectReportRowResponse.From).ToList()
    };
}
