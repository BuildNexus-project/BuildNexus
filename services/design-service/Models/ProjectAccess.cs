namespace BuildNexus.DesignService.Models;

/// <summary>
/// What the Project Service said when asked whether the caller may touch a
/// given project.
/// </summary>
public enum ProjectAccessOutcome
{
    /// <summary>The caller is party to the project — its client, assigned staff, or an Admin.</summary>
    Allowed,

    /// <summary>No project with that id.</summary>
    NotFound,

    /// <summary>The project exists, but the caller is not party to it.</summary>
    Forbidden,

    /// <summary>The Project Service could not be reached or did not answer usefully.</summary>
    Unavailable
}

/// <summary>
/// The answer to "may this caller touch this project", as the Project Service
/// gave it — relayed rather than re-decided, since that service owns the fact.
/// </summary>
public sealed class ProjectAccess
{
    private ProjectAccess(ProjectAccessOutcome outcome, Guid? assignedArchitectId)
    {
        Outcome = outcome;
        AssignedArchitectId = assignedArchitectId;
    }

    public ProjectAccessOutcome Outcome { get; }

    /// <summary>
    /// The Architect assigned to the project, or <c>null</c> if nobody is.
    /// Meaningful only when <see cref="Outcome"/> is
    /// <see cref="ProjectAccessOutcome.Allowed"/>.
    /// </summary>
    /// <remarks>
    /// Every project is unassigned today — no story has delivered staff
    /// assignment yet. Carried through so an upload endpoint can tighten to
    /// "the assigned Architect only" the moment that lands, without another
    /// round trip.
    /// </remarks>
    public Guid? AssignedArchitectId { get; }

    public bool IsAllowed => Outcome == ProjectAccessOutcome.Allowed;

    public static ProjectAccess Allowed(Guid? assignedArchitectId) =>
        new(ProjectAccessOutcome.Allowed, assignedArchitectId);

    public static readonly ProjectAccess NotFound = new(ProjectAccessOutcome.NotFound, null);

    public static readonly ProjectAccess Forbidden = new(ProjectAccessOutcome.Forbidden, null);

    public static readonly ProjectAccess Unavailable = new(ProjectAccessOutcome.Unavailable, null);
}
