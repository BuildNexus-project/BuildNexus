using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// <c>GET /api/projects/reports/status</c> (US-18): every project grouped by its
/// current status.
/// </summary>
public class ProjectStatusReportResponse
{
    /// <summary>
    /// When the report was produced. A pipeline changes by the hour, so a report
    /// that is printed or exported should say which moment it describes.
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    public int TotalProjects { get; set; }

    public decimal TotalBudget { get; set; }

    /// <summary>One group per reported status, in lifecycle order.</summary>
    public IReadOnlyList<ProjectStatusGroupResponse> Groups { get; set; } = [];

    public static ProjectStatusReportResponse From(ProjectStatusReport report, DateTime generatedAt) => new()
    {
        GeneratedAt = generatedAt,
        TotalProjects = report.TotalProjects,
        TotalBudget = report.TotalBudget,
        Groups = report.Groups.Select(ProjectStatusGroupResponse.From).ToList()
    };
}
