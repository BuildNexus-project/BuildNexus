using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>Read-only query behind the Admin project status report (US-18).</summary>
/// <remarks>
/// Kept apart from <see cref="IProjectRepository"/>, the same way the other
/// services keep their report queries apart from the path a request takes:
/// this is a different read shape, and a report query has no business being
/// reachable from a handler that writes.
/// </remarks>
public interface IProjectReportRepository
{
    /// <summary>
    /// Every project matching <paramref name="filter"/>, newest first. Filtering
    /// is done by the database, so a narrow report does not read the whole
    /// pipeline to throw most of it away. Grouping by status is not — that is
    /// applied to what this returns.
    /// </summary>
    Task<IReadOnlyList<ProjectReportRow>> ListAsync(
        ProjectReportFilter filter,
        CancellationToken cancellationToken = default);
}
