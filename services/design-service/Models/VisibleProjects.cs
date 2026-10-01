namespace BuildNexus.DesignService.Models;

/// <summary>
/// The answer to "which projects may this caller see", as the Project Service
/// gave it — relayed rather than re-decided, since that service owns who is on a
/// project.
/// </summary>
/// <remarks>
/// The same shape <see cref="ProjectAccess"/> uses for a single project: either
/// an answer, or an admission that there is none because the Project Service
/// could not be reached. An empty list is an answer — a Client with nothing under
/// way — and must not be mistaken for the second.
/// </remarks>
public sealed class VisibleProjects
{
    /// <summary>Terminal states, which no dashboard has any work left to show for.</summary>
    private static readonly string[] FinishedStatuses = ["Completed", "Cancelled"];

    private VisibleProjects(bool isAvailable, IReadOnlyList<VisibleProject> projects)
    {
        IsAvailable = isAvailable;
        Projects = projects;
    }

    /// <summary><c>false</c> when the Project Service could not be reached or did not answer usefully.</summary>
    public bool IsAvailable { get; }

    /// <summary>The projects the caller may see, in the order the Project Service listed them — most recently moved first. Empty when <see cref="IsAvailable"/> is <c>false</c>.</summary>
    public IReadOnlyList<VisibleProject> Projects { get; }

    /// <summary>
    /// The ids of the projects that still have work in them — everything but a
    /// finished or cancelled project — in the same order.
    /// </summary>
    public IReadOnlyList<Guid> ActiveProjectIds() =>
        Projects
            .Where(project => !FinishedStatuses.Contains(project.Status, StringComparer.OrdinalIgnoreCase))
            .Select(project => project.Id)
            .ToList();

    public static VisibleProjects Available(IReadOnlyList<VisibleProject> projects) => new(true, projects);

    public static readonly VisibleProjects Unavailable = new(false, []);
}
