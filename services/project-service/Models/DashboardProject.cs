namespace BuildNexus.ProjectService.Models;

/// <summary>
/// One project as a role dashboard lists it (US-21) — enough to recognise it and
/// see where it stands, and no more.
/// </summary>
/// <remarks>
/// Narrower than <see cref="Project"/> for the same reason
/// <see cref="ProjectReportRow"/> is: a dashboard card names a project and its
/// status, so the requirements, the budget and the assigned staff stay out of
/// the query rather than being read and thrown away.
/// </remarks>
public class DashboardProject
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public ProjectStatus Status { get; set; }

    /// <summary>
    /// When it last moved, which is what puts the projects somebody is actually
    /// working on at the top of the list.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
