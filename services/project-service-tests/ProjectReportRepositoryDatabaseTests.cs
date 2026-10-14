using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The Admin status report's query (US-18), run against real MySQL: the status
/// list and the date range are SQL, so the SQL is what has to be shown to work.
/// </summary>
/// <remarks>
/// Needs <c>project-db</c> running — see <see cref="ProjectDatabaseFixture"/>.
/// The development database may hold other projects, so every assertion looks
/// only at the ones this test created, told apart by a name unique to the run.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ProjectDatabaseCollection.Name)]
public class ProjectReportRepositoryDatabaseTests
{
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");

    /// <summary>
    /// A window far from any real data. Created dates are placed inside it so a
    /// date-range test cannot be disturbed by whatever else the database holds.
    /// </summary>
    private static readonly DateTime WindowStart = new(2031, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly ProjectDatabaseFixture _fixture;

    /// <summary>Unique to this test: xUnit builds the class once per test method.</summary>
    private readonly string _tag = $"{ProjectDatabaseFixture.TestProjectPrefix}report-{Guid.NewGuid():N}";

    public ProjectReportRepositoryDatabaseTests(ProjectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task With_no_filter_returns_projects_of_every_status()
    {
        foreach (var status in Enum.GetValues<ProjectStatus>())
        {
            await CreateProjectAsync(status, WindowStart);
        }

        var rows = await ListMineAsync(new ProjectReportFilter());

        Assert.Equal(Enum.GetValues<ProjectStatus>().Length, rows.Count);
        Assert.Equal(
            Enum.GetValues<ProjectStatus>().OrderBy(s => s),
            rows.Select(r => r.Status).OrderBy(s => s));
    }

    [Fact]
    public async Task A_status_filter_returns_only_those_statuses()
    {
        await CreateProjectAsync(ProjectStatus.Pending, WindowStart);
        await CreateProjectAsync(ProjectStatus.Designing, WindowStart);
        await CreateProjectAsync(ProjectStatus.Construction, WindowStart);
        await CreateProjectAsync(ProjectStatus.Completed, WindowStart);

        var rows = await ListMineAsync(new ProjectReportFilter
        {
            Statuses = [ProjectStatus.Designing, ProjectStatus.Completed]
        });

        Assert.Equal(
            [ProjectStatus.Designing, ProjectStatus.Completed],
            rows.Select(r => r.Status).OrderBy(s => s));
    }

    [Fact]
    public async Task An_empty_status_selection_matches_nothing()
    {
        await CreateProjectAsync(ProjectStatus.Pending, WindowStart);

        var rows = await ListMineAsync(new ProjectReportFilter { Statuses = [] });

        Assert.Empty(rows);
    }

    [Fact]
    public async Task A_date_range_includes_both_end_days_whole_and_excludes_the_days_outside()
    {
        var from = DateOnly.FromDateTime(WindowStart);
        var to = from.AddDays(2);

        var dayBefore = await CreateProjectAsync(ProjectStatus.Pending, WindowStart.AddSeconds(-1));
        var firstSecond = await CreateProjectAsync(ProjectStatus.Pending, WindowStart);
        var lastSecond = await CreateProjectAsync(ProjectStatus.Pending, WindowStart.AddDays(3).AddSeconds(-1));
        var dayAfter = await CreateProjectAsync(ProjectStatus.Pending, WindowStart.AddDays(3));

        var rows = await ListMineAsync(new ProjectReportFilter { From = from, To = to });

        var ids = rows.Select(r => r.Id).ToHashSet();
        Assert.Contains(firstSecond.Id, ids);
        Assert.Contains(lastSecond.Id, ids);
        Assert.DoesNotContain(dayBefore.Id, ids);
        Assert.DoesNotContain(dayAfter.Id, ids);
    }

    [Fact]
    public async Task A_status_and_a_date_range_narrow_together()
    {
        var from = DateOnly.FromDateTime(WindowStart);

        var wanted = await CreateProjectAsync(ProjectStatus.Construction, WindowStart.AddDays(1));
        await CreateProjectAsync(ProjectStatus.Pending, WindowStart.AddDays(1));
        await CreateProjectAsync(ProjectStatus.Construction, WindowStart.AddDays(-10));

        var rows = await ListMineAsync(new ProjectReportFilter
        {
            Statuses = [ProjectStatus.Construction],
            From = from
        });

        var only = Assert.Single(rows);
        Assert.Equal(wanted.Id, only.Id);
    }

    [Fact]
    public async Task Rows_come_back_newest_first()
    {
        var older = await CreateProjectAsync(ProjectStatus.Pending, WindowStart);
        var newer = await CreateProjectAsync(ProjectStatus.Pending, WindowStart.AddDays(1));

        var rows = await ListMineAsync(new ProjectReportFilter());

        Assert.Equal([newer.Id, older.Id], rows.Select(r => r.Id));
    }

    [Fact]
    public async Task A_row_carries_the_fields_the_report_shows()
    {
        var created = await CreateProjectAsync(ProjectStatus.Designing, WindowStart);

        var row = Assert.Single(await ListMineAsync(new ProjectReportFilter()));

        Assert.Equal(created.Id, row.Id);
        Assert.Equal(created.Name, row.Name);
        Assert.Equal(created.Location, row.Location);
        Assert.Equal(ProjectStatus.Designing, row.Status);
        Assert.Equal(created.Budget, row.Budget);
        Assert.Equal(WindowStart, row.CreatedAt);
    }

    /// <summary>The report's rows, cut down to the projects this test created.</summary>
    private async Task<IReadOnlyList<ProjectReportRow>> ListMineAsync(ProjectReportFilter filter)
    {
        var rows = await _fixture.ReportRepository.ListAsync(filter);

        return rows.Where(r => r.Name.StartsWith(_tag, StringComparison.Ordinal)).ToList();
    }

    private async Task<Project> CreateProjectAsync(ProjectStatus status, DateTime createdAtUtc)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = ClientId,
            Name = $"{_tag}-{Guid.NewGuid():N}",
            Location = "Galle",
            LandSizePerches = 25.5m,
            Budget = 18_500_000m,
            Floors = 2,
            Bedrooms = 4,
            Bathrooms = 3,
            GarageSpaces = 2,
            OtherRequirements = null,
            Status = status,
            CreatedAt = createdAtUtc,
            UpdatedAt = createdAtUtc
        };

        await _fixture.Repository.InsertAsync(
            project, ProjectStatusChange.ForCreation(project, PlatformRoles.Client), []);

        return project;
    }
}
