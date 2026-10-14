using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The role-dashboard queries (US-21), run against real MySQL: which projects a
/// person is on, which statuses count as active, and the status tally are all
/// SQL, so the SQL is what has to be shown to work.
/// </summary>
/// <remarks>
/// Needs <c>project-db</c> running — see <see cref="ProjectDatabaseFixture"/>.
/// The development database may hold other projects. The per-person lists are
/// narrowed by an id no other row can carry, so they are unaffected; the
/// system-wide tally cannot be, so it is asserted as a difference before and after.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ProjectDatabaseCollection.Name)]
public class ProjectDashboardRepositoryDatabaseTests
{
    private static readonly DateTime Moment = new(2031, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly ProjectDatabaseFixture _fixture;

    /// <summary>Unique to this test: xUnit builds the class once per test method.</summary>
    private readonly string _tag = $"{ProjectDatabaseFixture.TestProjectPrefix}dashboard-{Guid.NewGuid():N}";

    /// <summary>A Client and an Architect no other test or row shares.</summary>
    private readonly Guid _clientId = Guid.NewGuid();

    private readonly Guid _architectId = Guid.NewGuid();

    public ProjectDashboardRepositoryDatabaseTests(ProjectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------------------------ Client ----

    [Fact]
    public async Task A_client_sees_only_the_projects_they_submitted()
    {
        var mine = await CreateProjectAsync(ProjectStatus.Designing, _clientId);
        await CreateProjectAsync(ProjectStatus.Designing, Guid.NewGuid());

        var projects = await _fixture.DashboardRepository.ListActiveForClientAsync(_clientId);

        Assert.Equal([mine.Id], projects.Select(p => p.Id));
    }

    [Fact]
    public async Task A_clients_active_projects_leave_out_completed_and_cancelled()
    {
        foreach (var status in Enum.GetValues<ProjectStatus>())
        {
            await CreateProjectAsync(status, _clientId);
        }

        var projects = await _fixture.DashboardRepository.ListActiveForClientAsync(_clientId);

        Assert.Equal(
            [ProjectStatus.Pending, ProjectStatus.Designing, ProjectStatus.DesignApproved, ProjectStatus.Construction],
            projects.Select(p => p.Status).OrderBy(s => s));
    }

    [Fact]
    public async Task A_client_with_no_projects_gets_an_empty_list()
    {
        var projects = await _fixture.DashboardRepository.ListActiveForClientAsync(_clientId);

        Assert.Empty(projects);
    }

    [Fact]
    public async Task Projects_come_back_with_the_most_recently_moved_first()
    {
        var older = await CreateProjectAsync(ProjectStatus.Pending, _clientId, updatedAtUtc: Moment);
        var newer = await CreateProjectAsync(ProjectStatus.Pending, _clientId, updatedAtUtc: Moment.AddHours(3));

        var projects = await _fixture.DashboardRepository.ListActiveForClientAsync(_clientId);

        Assert.Equal([newer.Id, older.Id], projects.Select(p => p.Id));
    }

    [Fact]
    public async Task Carries_what_a_dashboard_card_needs()
    {
        var project = await CreateProjectAsync(ProjectStatus.Construction, _clientId);

        var listed = Assert.Single(await _fixture.DashboardRepository.ListActiveForClientAsync(_clientId));

        Assert.Equal(project.Id, listed.Id);
        Assert.Equal(project.Name, listed.Name);
        Assert.Equal("Galle", listed.Location);
        Assert.Equal(ProjectStatus.Construction, listed.Status);
        Assert.Equal(Moment, listed.UpdatedAt);
    }

    // --------------------------------------------------------- Architect ----

    [Fact]
    public async Task An_architect_sees_only_the_projects_they_are_assigned_to()
    {
        var assigned = await CreateProjectAsync(ProjectStatus.Designing, _clientId);
        await AssignArchitectAsync(assigned, _architectId);
        var someoneElses = await CreateProjectAsync(ProjectStatus.Designing, _clientId);
        await AssignArchitectAsync(someoneElses, Guid.NewGuid());
        await CreateProjectAsync(ProjectStatus.Pending, _clientId);

        var projects = await _fixture.DashboardRepository.ListActiveForArchitectAsync(_architectId);

        Assert.Equal([assigned.Id], projects.Select(p => p.Id));
    }

    [Fact]
    public async Task An_architects_active_projects_leave_out_completed_and_cancelled()
    {
        var active = await CreateProjectAsync(ProjectStatus.DesignApproved, _clientId);
        var completed = await CreateProjectAsync(ProjectStatus.Completed, _clientId);
        var cancelled = await CreateProjectAsync(ProjectStatus.Cancelled, _clientId);

        foreach (var project in new[] { active, completed, cancelled })
        {
            await AssignArchitectAsync(project, _architectId);
        }

        var projects = await _fixture.DashboardRepository.ListActiveForArchitectAsync(_architectId);

        Assert.Equal([active.Id], projects.Select(p => p.Id));
    }

    [Fact]
    public async Task Being_the_client_of_a_project_does_not_put_it_on_an_architects_list()
    {
        // The Client and Architect columns are different people. The same id in
        // the Client column must not surface a project as "assigned".
        await CreateProjectAsync(ProjectStatus.Designing, _architectId);

        Assert.Empty(await _fixture.DashboardRepository.ListActiveForArchitectAsync(_architectId));
    }

    // ------------------------------------------------------------- Admin ----

    [Fact]
    public async Task The_status_tally_counts_every_project_in_its_status()
    {
        var before = await TallyAsync();

        await CreateProjectAsync(ProjectStatus.Pending, _clientId);
        await CreateProjectAsync(ProjectStatus.Pending, _clientId);
        await CreateProjectAsync(ProjectStatus.Construction, _clientId);
        await CreateProjectAsync(ProjectStatus.Cancelled, _clientId);

        var after = await TallyAsync();

        Assert.Equal(2, after[ProjectStatus.Pending] - before[ProjectStatus.Pending]);
        Assert.Equal(1, after[ProjectStatus.Construction] - before[ProjectStatus.Construction]);
        Assert.Equal(1, after[ProjectStatus.Cancelled] - before[ProjectStatus.Cancelled]);
        Assert.Equal(0, after[ProjectStatus.Designing] - before[ProjectStatus.Designing]);
    }

    [Fact]
    public async Task The_tally_is_across_every_client_not_one()
    {
        var before = await TallyAsync();

        await CreateProjectAsync(ProjectStatus.Designing, Guid.NewGuid());
        await CreateProjectAsync(ProjectStatus.Designing, Guid.NewGuid());

        var after = await TallyAsync();

        Assert.Equal(2, after[ProjectStatus.Designing] - before[ProjectStatus.Designing]);
    }

    // ------------------------------------------------------------ helpers ----

    /// <summary>
    /// Every status with its count, zero where the query returned no row — the
    /// query leaves an empty status out, and the tests want to subtract.
    /// </summary>
    private async Task<Dictionary<ProjectStatus, int>> TallyAsync()
    {
        var counts = await _fixture.DashboardRepository.CountByStatusAsync();

        return Enum.GetValues<ProjectStatus>()
            .ToDictionary(
                status => status,
                status => counts.Where(c => c.Status == status).Sum(c => c.Count));
    }

    private async Task AssignArchitectAsync(Project project, Guid architectId)
    {
        var assigned = await _fixture.Repository.AssignArchitectAsync(project.Id, architectId, null, [], Moment);

        Assert.True(assigned);
    }

    private async Task<Project> CreateProjectAsync(
        ProjectStatus status,
        Guid clientId,
        DateTime? updatedAtUtc = null)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
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
            CreatedAt = Moment,
            UpdatedAt = updatedAtUtc ?? Moment
        };

        await _fixture.Repository.InsertAsync(
            project, ProjectStatusChange.ForCreation(project, PlatformRoles.Client), []);

        return project;
    }
}
