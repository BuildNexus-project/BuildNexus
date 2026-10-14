using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IConstructionDashboardRepository"/> that answers with whatever a
/// test asks for and remembers what it was asked, so
/// <see cref="Controllers.DashboardController"/> can be checked without MySQL.
/// </summary>
/// <remarks>
/// It does not scope by Client or by project, or judge a phase — that is SQL, and
/// <see cref="ConstructionDashboardRepositoryDatabaseTests"/> covers it against the
/// real engine. What the controller tests need to know is which Client or which projects
/// the endpoint took, what day and what cap it asked for, and what it did with the rows
/// that came back.
/// </remarks>
public sealed class FakeConstructionDashboardRepository : IConstructionDashboardRepository
{
    public IReadOnlyList<ConstructionProgressReportRow> ClientProgress { get; set; } = [];

    public IReadOnlyList<ConstructionProgressReportRow> ActiveBuilds { get; set; } = [];

    public OutstandingMilestones Outstanding { get; set; } = new() { TotalCount = 0, OverdueCount = 0, Items = [] };

    /// <summary>The Client the last progress query was for, or <c>null</c> if none has been made.</summary>
    public Guid? LastClientId { get; private set; }

    /// <summary>The projects the last Project Manager query was for, or <c>null</c> if none has been made.</summary>
    public IReadOnlyList<Guid>? LastProjectIds { get; private set; }

    /// <summary>The cap on the last outstanding-milestones query, or <c>null</c> if none has been made.</summary>
    public int? LastLimit { get; private set; }

    /// <summary>The day the last outstanding-milestones query judged lateness against, or <c>null</c> if none has been made.</summary>
    public DateOnly? LastToday { get; private set; }

    /// <summary>How many queries of any kind have run.</summary>
    public int Queries { get; private set; }

    public Task<IReadOnlyList<ConstructionProgressReportRow>> ListProgressForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastClientId = clientId;

        return Task.FromResult(ClientProgress);
    }

    public Task<IReadOnlyList<ConstructionProgressReportRow>> ListActiveBuildsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastProjectIds = projectIds.ToList();

        return Task.FromResult(ActiveBuilds);
    }

    public Task<OutstandingMilestones> GetOutstandingMilestonesAsync(
        IReadOnlyCollection<Guid> projectIds,
        int limit,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastProjectIds = projectIds.ToList();
        LastLimit = limit;
        LastToday = today;

        return Task.FromResult(Outstanding);
    }
}
