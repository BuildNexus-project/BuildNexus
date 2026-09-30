using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The report query held in memory: hands back whatever it was seeded with and
/// remembers the filter it was asked for.
/// </summary>
/// <remarks>
/// It does not apply the filter — that is SQL, and
/// <c>ProjectReportRepositoryDatabaseTests</c> covers it against the real thing.
/// What the controller tests need to know is which filter the endpoint built
/// from the request, and what it did with the rows that came back.
/// </remarks>
public sealed class FakeProjectReportRepository : IProjectReportRepository
{
    private readonly List<ProjectReportRow> _rows = [];

    /// <summary>The filter of the last query, or <c>null</c> if none has been made.</summary>
    public ProjectReportFilter? LastFilter { get; private set; }

    /// <summary>How many times the query has run.</summary>
    public int Queries { get; private set; }

    public void Seed(params ProjectReportRow[] rows) => _rows.AddRange(rows);

    public Task<IReadOnlyList<ProjectReportRow>> ListAsync(
        ProjectReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastFilter = filter;

        return Task.FromResult<IReadOnlyList<ProjectReportRow>>(_rows.ToList());
    }
}
