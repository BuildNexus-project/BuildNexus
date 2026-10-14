using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>Read-only queries behind the role dashboards (US-21).</summary>
/// <remarks>
/// Kept apart from <see cref="IProjectRepository"/> the same way the report query
/// is: these are aggregate read shapes, and a handler that writes has no business
/// being able to reach them.
/// <para>
/// "Active" means the two terminal statuses are out: <see cref="ProjectStatus.Completed"/>
/// is finished and <see cref="ProjectStatus.Cancelled"/> was closed out, so neither
/// is work anybody still has to do. It is the same line the project list already
/// draws for cancelled projects, drawn one step earlier.
/// </para>
/// </remarks>
public interface IProjectDashboardRepository
{
    /// <summary>
    /// The active projects the Client submitted, most recently moved first.
    /// </summary>
    Task<IReadOnlyList<DashboardProject>> ListActiveForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The active projects the Architect is assigned to, most recently moved first.
    /// </summary>
    Task<IReadOnlyList<DashboardProject>> ListActiveForArchitectAsync(
        Guid architectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many projects are in each status, across the whole system. A status
    /// nothing is in is absent from the result rather than present with a zero —
    /// the caller decides how to present an empty status.
    /// </summary>
    Task<IReadOnlyList<ProjectStatusCount>> CountByStatusAsync(
        CancellationToken cancellationToken = default);
}
