namespace BuildNexus.ProjectService.Models;

/// <summary>
/// The moves a project's status is allowed to make:
/// <c>Pending → Designing → DesignApproved → Construction → Completed</c>.
/// </summary>
/// <remarks>
/// Kept apart from the controller so the rule can be read, and tested, on its
/// own — it is the whole of the third US-06 acceptance bullet, and it is the
/// one piece of this story that is neither HTTP nor SQL.
/// <para>
/// Strictly forward and one step at a time. A project cannot skip a stage (a
/// build does not start before a design is approved), cannot go back (the
/// history is the record of what happened, not somewhere to undo it), and
/// cannot be "changed" to the status it already holds — a no-op change would
/// write a history row saying nothing happened.
/// </para>
/// </remarks>
public static class ProjectStatusTransitions
{
    /// <summary>
    /// What may follow each status. <see cref="ProjectStatus.Completed"/> maps
    /// to nothing on purpose: a finished project is finished.
    /// </summary>
    private static readonly IReadOnlyDictionary<ProjectStatus, IReadOnlyList<ProjectStatus>> Allowed =
        new Dictionary<ProjectStatus, IReadOnlyList<ProjectStatus>>
        {
            [ProjectStatus.Pending] = [ProjectStatus.Designing],
            [ProjectStatus.Designing] = [ProjectStatus.DesignApproved],
            [ProjectStatus.DesignApproved] = [ProjectStatus.Construction],
            [ProjectStatus.Construction] = [ProjectStatus.Completed],
            [ProjectStatus.Completed] = []
        };

    /// <summary>
    /// The statuses a project in <paramref name="current"/> may move to — one,
    /// or none at the end of the lifecycle.
    /// </summary>
    /// <remarks>
    /// Also what the UI offers: a screen that only lists the moves that will be
    /// accepted cannot invite somebody into a 400.
    /// </remarks>
    public static IReadOnlyList<ProjectStatus> NextFrom(ProjectStatus current) =>
        Allowed.TryGetValue(current, out var next) ? next : [];

    /// <summary>Whether a project may move from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool IsAllowed(ProjectStatus from, ProjectStatus to) =>
        NextFrom(from).Contains(to);
}
