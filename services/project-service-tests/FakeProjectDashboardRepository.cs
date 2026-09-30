using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The dashboard queries held in memory: hands back whatever it was seeded with
/// and remembers whose dashboard it was asked for.
/// </summary>
/// <remarks>
/// It does not filter by person or status — that is SQL, and
/// <c>ProjectDashboardRepositoryDatabaseTests</c> covers it against the real
/// thing. What the controller tests need to know is which id the endpoint took
/// from the token, and what it did with the rows that came back.
/// </remarks>
public sealed class FakeProjectDashboardRepository : IProjectDashboardRepository
{
    private readonly List<DashboardProject> _projects = [];
    private readonly List<ProjectStatusCount> _counts = [];

    /// <summary>The Client the last Client query was for, or <c>null</c> if none has been made.</summary>
    public Guid? LastClientId { get; private set; }

    /// <summary>The Architect the last Architect query was for, or <c>null</c> if none has been made.</summary>
    public Guid? LastArchitectId { get; private set; }

    /// <summary>How many queries of any kind have run.</summary>
    public int Queries { get; private set; }

    public void SeedProjects(params DashboardProject[] projects) => _projects.AddRange(projects);

    public void SeedCounts(params ProjectStatusCount[] counts) => _counts.AddRange(counts);

    public Task<IReadOnlyList<DashboardProject>> ListActiveForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastClientId = clientId;

        return Task.FromResult<IReadOnlyList<DashboardProject>>(_projects.ToList());
    }

    public Task<IReadOnlyList<DashboardProject>> ListActiveForArchitectAsync(
        Guid architectId,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastArchitectId = architectId;

        return Task.FromResult<IReadOnlyList<DashboardProject>>(_projects.ToList());
    }

    public Task<IReadOnlyList<ProjectStatusCount>> CountByStatusAsync(
        CancellationToken cancellationToken = default)
    {
        Queries++;

        return Task.FromResult<IReadOnlyList<ProjectStatusCount>>(_counts.ToList());
    }
}
