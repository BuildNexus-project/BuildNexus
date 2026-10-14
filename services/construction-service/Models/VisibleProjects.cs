namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The answer to "which projects may this caller see", as the Project Service gave it —
/// relayed rather than re-decided, since that service owns who is on a project.
/// </summary>
/// <remarks>
/// Either an answer, or an admission that there is none because the Project Service could
/// not be reached. An empty list is an answer — a Project Manager nobody has assigned yet —
/// and must not be mistaken for the second.
/// <para>
/// The Project Service already leaves cancelled projects out of the list, and for a Project
/// Manager it is the projects they are assigned to. Nothing is filtered further here: a
/// project whose status is <c>Completed</c> may still have a build awaiting handover, so its
/// status is not this service's to second-guess.
/// </para>
/// </remarks>
public sealed class VisibleProjects
{
    private VisibleProjects(bool isAvailable, IReadOnlyList<VisibleProject> projects)
    {
        IsAvailable = isAvailable;
        Projects = projects;
    }

    /// <summary><c>false</c> when the Project Service could not be reached or did not answer usefully.</summary>
    public bool IsAvailable { get; }

    /// <summary>The projects the caller may see. Empty when <see cref="IsAvailable"/> is <c>false</c>.</summary>
    public IReadOnlyList<VisibleProject> Projects { get; }

    /// <summary>Every project's id, in the order the Project Service listed them.</summary>
    public IReadOnlyList<Guid> Ids() => Projects.Select(project => project.Id).ToList();

    /// <summary>The name of a project it listed, or <c>null</c> for one it did not.</summary>
    public string? NameOf(Guid projectId) =>
        Projects.FirstOrDefault(project => project.Id == projectId)?.Name;

    public static VisibleProjects Available(IReadOnlyList<VisibleProject> projects) => new(true, projects);

    public static readonly VisibleProjects Unavailable = new(false, []);
}
