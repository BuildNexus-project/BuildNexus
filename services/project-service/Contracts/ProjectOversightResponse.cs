using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// <c>GET /api/projects/oversight</c> (US-38): every project on the platform, for
/// the Admin's single oversight screen.
/// </summary>
public class ProjectOversightResponse
{
    /// <summary>
    /// The moment the stalled flags were judged at. "Stalled" depends on today's
    /// date, so a list that is read later should say which moment it describes.
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// How many days without movement make an open project stalled, so the screen
    /// can say so without keeping its own copy of the number.
    /// </summary>
    public int StalledAfterDays { get; set; }

    public int TotalProjects { get; set; }

    public int StalledCount { get; set; }

    /// <summary>Every project, cancelled ones included, newest submitted first.</summary>
    public IReadOnlyList<OversightProjectResponse> Projects { get; set; } = [];

    public static ProjectOversightResponse From(
        IReadOnlyList<Project> projects,
        IReadOnlyDictionary<Guid, string> names,
        DateTime generatedAt)
    {
        var rows = projects.Select(project => OversightProjectResponse.From(project, names, generatedAt)).ToList();

        return new ProjectOversightResponse
        {
            GeneratedAt = generatedAt,
            StalledAfterDays = ProjectStallPolicy.StalledAfterDays,
            TotalProjects = rows.Count,
            StalledCount = rows.Count(row => row.IsStalled),
            Projects = rows
        };
    }
}
