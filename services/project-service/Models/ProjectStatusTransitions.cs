namespace BuildNexus.ProjectService.Models;

/// <summary>
/// The moves a project's status is allowed to make:
/// <c>Pending → Designing → DesignApproved → Construction → Completed</c>,
/// plus cancellation out of any status before <c>Construction</c> (US-08).
/// </summary>
/// <remarks>
/// Kept apart from the controller so the rule can be read, and tested, on its
/// own — it is the whole of the third US-06 acceptance bullet, and it is the
/// one piece of this story that is neither HTTP nor SQL.
/// <para>
/// The forward lifecycle is strictly forward and one step at a time. A project
/// cannot skip a stage (a build does not start before a design is approved),
/// cannot go back (the history is the record of what happened, not somewhere to
/// undo it), and cannot be "changed" to the status it already holds — a no-op
/// change would write a history row saying nothing happened.
/// </para>
/// <para>
/// Cancellation is a move the forward table does not describe — it is not a
/// step in the lifecycle, and who may make it and when are different rules
/// (US-08). <see cref="CanCancelFrom"/> answers that; <see cref="IsAllowed"/>
/// stays about the forward path only.
/// </para>
/// </remarks>
public static class ProjectStatusTransitions
{
    /// <summary>
    /// What may follow each status on the forward path.
    /// <see cref="ProjectStatus.Completed"/> and
    /// <see cref="ProjectStatus.Cancelled"/> both map to nothing on purpose:
    /// one is finished, the other is closed out, and neither moves again.
    /// </summary>
    private static readonly IReadOnlyDictionary<ProjectStatus, IReadOnlyList<ProjectStatus>> Allowed =
        new Dictionary<ProjectStatus, IReadOnlyList<ProjectStatus>>
        {
            [ProjectStatus.Pending] = [ProjectStatus.Designing],
            [ProjectStatus.Designing] = [ProjectStatus.DesignApproved],
            [ProjectStatus.DesignApproved] = [ProjectStatus.Construction],
            [ProjectStatus.Construction] = [ProjectStatus.Completed],
            [ProjectStatus.Completed] = [],
            [ProjectStatus.Cancelled] = []
        };

    /// <summary>
    /// The statuses a project can still be cancelled out of — US-08: any before
    /// <see cref="ProjectStatus.Construction"/>.
    /// </summary>
    /// <remarks>
    /// Once a build has started there is work on the ground to account for, so
    /// cancellation stops being a clean close-out; a finished project has
    /// nothing to cancel; and an already-cancelled one is done.
    /// </remarks>
    private static readonly IReadOnlySet<ProjectStatus> CancellableFrom =
        new HashSet<ProjectStatus>
        {
            ProjectStatus.Pending,
            ProjectStatus.Designing,
            ProjectStatus.DesignApproved
        };

    /// <summary>Whether a project in <paramref name="current"/> may still be cancelled.</summary>
    public static bool CanCancelFrom(ProjectStatus current) => CancellableFrom.Contains(current);

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
