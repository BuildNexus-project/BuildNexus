using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>Read-only queries behind the role dashboards (US-21).</summary>
/// <remarks>
/// Kept apart from the milestone and phase repositories the same way the report
/// query is: these are aggregate read shapes, and a handler that writes has no
/// business being able to reach them.
/// <para>
/// Both progress queries answer with <see cref="ConstructionProgressReportRow"/> —
/// a project's phase, milestone breakdown and percentage is one shape wherever it
/// is shown, so a project's line on a dashboard and on the construction report
/// are the same fields computed the same way.
/// </para>
/// </remarks>
public interface IConstructionDashboardRepository
{
    /// <summary>
    /// The build progress of every project the Client owns that has milestones
    /// defined and has not been handed over (US-21 AC-1).
    /// </summary>
    /// <remarks>
    /// Ownership is this service's own record — the <c>project_owners</c> row the
    /// <c>ProjectCreated</c> consumer planted — so no other service is asked. A
    /// project whose build has not started is included with a <c>null</c> phase and
    /// zero progress: it is planned, and the Client should see that it is not yet
    /// under way. A Client with nothing planned gets an empty list.
    /// </remarks>
    Task<IReadOnlyList<ConstructionProgressReportRow>> ListProgressForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Of the given projects, the builds that are under way: those whose construction has
    /// started and has not been handed over (US-21 AC-3).
    /// </summary>
    /// <remarks>
    /// "Under way" is <c>Started</c> or <c>Completed</c> — a finished build still
    /// awaiting handover is still something the Project Manager has to act on.
    /// Narrower than the construction report's "active", which also lists planned
    /// builds nobody has started; a dashboard's "active construction" is the work
    /// actually in progress.
    /// <para>
    /// The projects are passed in rather than decided here: this service does not know which
    /// Project Manager runs a project, so the caller asks the Project Service which projects
    /// the Project Manager is assigned to and passes those. Most recently started first. An
    /// empty list of projects answers empty without a query.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ConstructionProgressReportRow>> ListActiveBuildsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The milestones still to finish on the given projects' builds that are under way
    /// (US-21 AC-3), with how many there are in all and how many of those are late.
    /// </summary>
    /// <param name="projectIds">The projects to look at — see <see cref="ListActiveBuildsAsync"/>.</param>
    /// <param name="limit">The most milestones to list. The counts are unaffected by it.</param>
    /// <param name="today">
    /// The day "late" is judged against: a milestone is overdue when its due date is before
    /// it. Passed in rather than read from the database clock, so the answer is for a day the
    /// caller chose and a test can fix it.
    /// </param>
    Task<OutstandingMilestones> GetOutstandingMilestonesAsync(
        IReadOnlyCollection<Guid> projectIds,
        int limit,
        DateOnly today,
        CancellationToken cancellationToken = default);
}
