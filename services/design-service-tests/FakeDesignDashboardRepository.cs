using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The dashboard queries held in memory: hands back whatever it was seeded with
/// and remembers which projects it was asked about.
/// </summary>
/// <remarks>
/// It does not filter by project or by latest version — that is SQL, and
/// <c>DesignDashboardRepositoryDatabaseTests</c> covers it against the real thing.
/// What the controller tests need to know is which project ids the endpoint took
/// from the Project Service's answer, and what it did with the rows that came back.
/// </remarks>
public sealed class FakeDesignDashboardRepository : IDesignDashboardRepository
{
    public IReadOnlyList<ProjectDesignTally> Tallies { get; set; } = [];

    public IReadOnlyList<PendingRevision> Revisions { get; set; } = [];

    /// <summary>The project ids of the last query of either kind, or <c>null</c> if none has been made.</summary>
    public IReadOnlyList<Guid>? LastProjectIds { get; private set; }

    /// <summary>How many queries of any kind have run.</summary>
    public int Queries { get; private set; }

    public Task<IReadOnlyList<ProjectDesignTally>> GetDesignTalliesAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastProjectIds = projectIds.ToList();

        return Task.FromResult(Tallies);
    }

    public Task<IReadOnlyList<PendingRevision>> ListPendingRevisionsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastProjectIds = projectIds.ToList();

        return Task.FromResult(Revisions);
    }
}
