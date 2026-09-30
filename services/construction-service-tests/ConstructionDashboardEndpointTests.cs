using System.Security.Claims;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The US-21 acceptance criteria this service owns, walked over fakes: a Client sees how far
/// each of their builds has got, and a Project Manager sees the builds under way and the
/// milestones still to finish on the projects they are assigned to.
/// </summary>
/// <remarks>
/// Who may call which endpoint is not decided in the action but by its role attribute, so
/// that is pinned in <see cref="EndpointRoleDeclarationTests"/>. The SQL — ownership scoping,
/// the "under way" gate, the outstanding and overdue counts — is proved in
/// <see cref="ConstructionDashboardRepositoryDatabaseTests"/>. What is under test here is the
/// endpoint's own decisions: whose id or which projects it scopes to, that it forwards the
/// caller's token, what day it judges lateness against, what it asks for, and how the rows
/// reach the wire.
/// </remarks>
public class ConstructionDashboardEndpointTests
{
    private const string Token = "caller-token";

    private static readonly Guid CallerId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid Villa = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid Cottage = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly DateTime Planned = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The day the endpoint believes it is: the tests' clock is fixed to it.</summary>
    private static readonly DateOnly Today = new(2026, 10, 15);

    // ------------------------------------------------ AC-1: Client's progress ----

    [Fact]
    public async Task A_client_sees_the_progress_of_each_of_their_projects()
    {
        var (controller, repository, _) = ControllerWith("Client");
        repository.ClientProgress =
        [
            Row(Villa, ConstructionPhaseStatus.Started, total: 4, completed: 1, inProgress: 2, notStarted: 1, percent: 25m),
            Row(Cottage, phase: null, total: 2, completed: 0, inProgress: 0, notStarted: 2, percent: 0m)
        ];

        var dashboard = await Client(controller);

        Assert.Equal([Villa, Cottage], dashboard.Projects.Select(p => p.ProjectId));

        var villa = dashboard.Projects[0];
        Assert.Equal(ConstructionPhaseStatus.Started, villa.PhaseStatus);
        Assert.Equal(4, villa.TotalMilestones);
        Assert.Equal(1, villa.CompletedMilestones);
        Assert.Equal(25m, villa.ProgressPercent);
    }

    [Fact]
    public async Task A_planned_project_whose_build_has_not_started_carries_a_null_phase()
    {
        var (controller, repository, _) = ControllerWith("Client");
        repository.ClientProgress = [Row(Villa, phase: null, total: 3, completed: 0, inProgress: 0, notStarted: 3, percent: 0m)];

        var project = Assert.Single((await Client(controller)).Projects);

        Assert.Null(project.PhaseStatus);
        Assert.Equal(0m, project.ProgressPercent);
    }

    [Fact]
    public async Task A_client_with_nothing_planned_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _, _) = ControllerWith("Client");

        Assert.Empty((await Client(controller)).Projects);
    }

    [Fact]
    public async Task The_client_query_is_for_the_caller_named_in_the_token()
    {
        var (controller, repository, _) = ControllerWith("Client");

        await controller.GetClientDashboard(default);

        Assert.Equal(CallerId, repository.LastClientId);
    }

    [Fact]
    public async Task A_client_token_with_no_usable_subject_is_refused_and_nothing_is_queried()
    {
        var (controller, repository, _) = ControllerWith("Client", subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetClientDashboard(default));

        Assert.Equal(0, repository.Queries);
    }

    [Fact]
    public async Task A_clients_dashboard_never_asks_the_project_service_anything()
    {
        // Ownership is this service's own record for a Client. Only a Project Manager's
        // dashboard needs the Project Service, because assignment is not stored here.
        var (controller, _, projects) = ControllerWith("Client");

        await controller.GetClientDashboard(default);

        Assert.Equal(0, projects.Calls);
    }

    // ---------------------------- AC-3: Project Manager's builds and milestones ----

    [Fact]
    public async Task A_project_manager_sees_the_builds_under_way_and_how_many_there_are()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"), (Cottage, "Hilltop cottage"));
        repository.ActiveBuilds =
        [
            Row(Villa, ConstructionPhaseStatus.Started, total: 5, completed: 2, inProgress: 1, notStarted: 2, percent: 40m),
            Row(Cottage, ConstructionPhaseStatus.Completed, total: 3, completed: 3, inProgress: 0, notStarted: 0, percent: 100m)
        ];

        var dashboard = await ProjectManager(controller);

        Assert.Equal(2, dashboard.ActiveBuildCount);
        Assert.Equal([Villa, Cottage], dashboard.ActiveBuilds.Select(b => b.ProjectId));

        var villa = dashboard.ActiveBuilds[0];
        Assert.Equal(ConstructionPhaseStatus.Started, villa.PhaseStatus);
        Assert.Equal(5, villa.TotalMilestones);
        Assert.Equal(2, villa.CompletedMilestones);
        Assert.Equal(40m, villa.ProgressPercent);
    }

    [Fact]
    public async Task Each_build_reports_how_many_of_its_milestones_are_still_to_finish()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"), (Cottage, "Hilltop cottage"));
        repository.ActiveBuilds =
        [
            Row(Villa, ConstructionPhaseStatus.Started, total: 5, completed: 2, inProgress: 1, notStarted: 2, percent: 40m),
            Row(Cottage, ConstructionPhaseStatus.Completed, total: 3, completed: 3, inProgress: 0, notStarted: 0, percent: 100m)
        ];

        var dashboard = await ProjectManager(controller);

        Assert.Equal([3, 0], dashboard.ActiveBuilds.Select(b => b.OutstandingMilestones));
    }

    [Fact]
    public async Task Each_build_and_milestone_names_its_project_from_the_project_services_answer()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"), (Cottage, "Hilltop cottage"));
        repository.ActiveBuilds = [Row(Cottage, ConstructionPhaseStatus.Started, 4, 1, 1, 2, 25m)];
        repository.Outstanding = Outstanding(1, 0, Milestone(Villa, "Walls", MilestoneStatus.InProgress));

        var dashboard = await ProjectManager(controller);

        Assert.Equal("Hilltop cottage", Assert.Single(dashboard.ActiveBuilds).ProjectName);
        Assert.Equal("Beachfront villa", Assert.Single(dashboard.MilestonesDue.Milestones).ProjectName);
    }

    [Fact]
    public async Task Only_the_projects_the_project_service_says_the_project_manager_is_on_are_asked_about()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"), (Cottage, "Hilltop cottage"));

        await controller.GetProjectManagerDashboard(default);

        Assert.Equal([Villa, Cottage], repository.LastProjectIds);
    }

    [Fact]
    public async Task A_project_whose_status_is_completed_is_still_asked_about_because_its_build_may_await_handover()
    {
        // The Project Service marks a project Completed when construction is marked complete,
        // which is before handover — so its status is not a reason to stop looking at the build.
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Cottage, "Hilltop cottage"));

        await controller.GetProjectManagerDashboard(default);

        Assert.Equal([Cottage], repository.LastProjectIds);
    }

    [Fact]
    public async Task The_project_managers_own_token_is_what_the_project_service_is_asked_with()
    {
        var (controller, _, projects) = ControllerWith("ProjectManager");

        await controller.GetProjectManagerDashboard(default);

        Assert.Equal(Token, projects.LastToken);
    }

    [Fact]
    public async Task A_project_manager_with_no_projects_assigned_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible();

        var dashboard = await ProjectManager(controller);

        Assert.Equal(0, dashboard.ActiveBuildCount);
        Assert.Empty(dashboard.ActiveBuilds);
        Assert.Equal(0, dashboard.MilestonesDue.TotalCount);
        Assert.Empty(dashboard.MilestonesDue.Milestones);
    }

    [Fact]
    public async Task A_project_service_that_cannot_answer_is_a_502_not_an_empty_dashboard()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = VisibleProjects.Unavailable;

        var result = await controller.GetProjectManagerDashboard(default);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Equal(0, repository.Queries);
    }

    [Fact]
    public async Task A_request_carrying_no_bearer_token_is_refused_before_anything_is_asked()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager", authorization: null);

        Assert.IsType<UnauthorizedResult>(await controller.GetProjectManagerDashboard(default));

        Assert.Equal(0, projects.Calls);
        Assert.Equal(0, repository.Queries);
    }

    // -------------------------------------------------------- milestones due ----

    [Fact]
    public async Task A_project_manager_sees_the_milestones_still_due_with_the_full_count()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"));
        repository.Outstanding = Outstanding(
            14, 0,
            Milestone(Villa, "Walls", MilestoneStatus.InProgress),
            Milestone(Villa, "Roof", MilestoneStatus.NotStarted));

        var due = (await ProjectManager(controller)).MilestonesDue;

        // The count is everything outstanding, not the length of the short list beside it.
        Assert.Equal(14, due.TotalCount);
        Assert.Equal(["Walls", "Roof"], due.Milestones.Select(m => m.Name));
    }

    [Fact]
    public async Task Each_milestone_due_carries_what_a_row_needs()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"));
        var walls = Milestone(Villa, "Walls", MilestoneStatus.InProgress);
        repository.Outstanding = Outstanding(1, 0, walls);

        var listed = Assert.Single((await ProjectManager(controller)).MilestonesDue.Milestones);

        Assert.Equal(walls.Id, listed.Id);
        Assert.Equal(Villa, listed.ProjectId);
        Assert.Equal("Walls", listed.Name);
        Assert.Equal(MilestoneStatus.InProgress, listed.Status);
        Assert.Equal(Planned, listed.CreatedAt);
    }

    [Fact]
    public async Task The_overdue_count_is_carried_beside_the_total()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"));
        repository.Outstanding = Outstanding(14, 3);

        var due = (await ProjectManager(controller)).MilestonesDue;

        Assert.Equal(14, due.TotalCount);
        Assert.Equal(3, due.OverdueCount);
    }

    [Fact]
    public async Task A_milestone_is_overdue_when_its_due_date_is_before_today_and_not_when_it_is_today_or_later()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"));
        repository.Outstanding = Outstanding(
            4, 1,
            Milestone(Villa, "Yesterday", MilestoneStatus.InProgress, Today.AddDays(-1)),
            Milestone(Villa, "Today", MilestoneStatus.InProgress, Today),
            Milestone(Villa, "Tomorrow", MilestoneStatus.NotStarted, Today.AddDays(1)),
            Milestone(Villa, "Undated", MilestoneStatus.NotStarted));

        var listed = (await ProjectManager(controller)).MilestonesDue.Milestones;

        Assert.Equal([true, false, false, false], listed.Select(m => m.IsOverdue));
    }

    [Fact]
    public async Task A_milestone_with_no_due_date_is_never_overdue_however_long_ago_it_was_planned()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"));
        repository.Outstanding = Outstanding(1, 0, Milestone(Villa, "Undated", MilestoneStatus.NotStarted));

        var listed = Assert.Single((await ProjectManager(controller)).MilestonesDue.Milestones);

        Assert.Null(listed.DueDate);
        Assert.False(listed.IsOverdue);
    }

    [Fact]
    public async Task Each_milestone_carries_its_due_date()
    {
        var (controller, repository, projects) = ControllerWith("ProjectManager");
        projects.Result = Visible((Villa, "Beachfront villa"));
        repository.Outstanding = Outstanding(
            1, 0, Milestone(Villa, "Walls", MilestoneStatus.InProgress, new DateOnly(2026, 11, 3)));

        var listed = Assert.Single((await ProjectManager(controller)).MilestonesDue.Milestones);

        Assert.Equal(new DateOnly(2026, 11, 3), listed.DueDate);
    }

    [Fact]
    public async Task Lateness_is_judged_against_todays_date_from_the_clock_and_that_day_is_what_the_query_is_told()
    {
        var (controller, repository, _) = ControllerWith("ProjectManager");

        await controller.GetProjectManagerDashboard(default);

        // The repository counts overdue against the day it is given, so the endpoint and the
        // per-milestone flag cannot disagree about what today is.
        Assert.Equal(Today, repository.LastToday);
    }

    [Fact]
    public async Task The_milestone_list_is_capped_at_the_size_the_endpoint_promises()
    {
        var (controller, repository, _) = ControllerWith("ProjectManager");

        await controller.GetProjectManagerDashboard(default);

        Assert.Equal(DashboardController.MilestonesDueListSize, repository.LastLimit);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ClientConstructionDashboardResponse> Client(DashboardController controller) =>
        Assert.IsType<ClientConstructionDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetClientDashboard(default)).Value);

    private static async Task<ProjectManagerConstructionDashboardResponse> ProjectManager(DashboardController controller) =>
        Assert.IsType<ProjectManagerConstructionDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetProjectManagerDashboard(default)).Value);

    private static (DashboardController Controller, FakeConstructionDashboardRepository Repository, FakeProjectDirectoryClient Projects)
        ControllerWith(string role, string? subject = null, string? authorization = $"Bearer {Token}")
    {
        var repository = new FakeConstructionDashboardRepository();
        var projects = new FakeProjectDirectoryClient();

        var httpContext = new DefaultHttpContext
        {
            // The claims a token validated by this service leaves behind: "sub" and "role" in
            // their short form, not rewritten into the WS-Federation URIs.
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject ?? CallerId.ToString()),
                new Claim("role", role)
            ], "TestAuth"))
        };

        if (authorization is not null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        var controller = new DashboardController(repository, projects, new FixedClock(Today))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, repository, projects);
    }

    private static VisibleProjects Visible(params (Guid Id, string Name)[] projects) =>
        VisibleProjects.Available(projects.Select(p => new VisibleProject(p.Id, p.Name)).ToList());

    private static ConstructionProgressReportRow Row(
        Guid projectId,
        ConstructionPhaseStatus? phase,
        int total,
        int completed,
        int inProgress,
        int notStarted,
        decimal percent) => new()
    {
        ProjectId = projectId,
        PhaseStatus = phase,
        TotalMilestones = total,
        CompletedMilestones = completed,
        InProgressMilestones = inProgress,
        NotStartedMilestones = notStarted,
        ProgressPercent = percent
    };

    private static OutstandingMilestones Outstanding(int total, int overdue, params OutstandingMilestone[] items) =>
        new() { TotalCount = total, OverdueCount = overdue, Items = items };

    private static OutstandingMilestone Milestone(
        Guid projectId,
        string name,
        MilestoneStatus status,
        DateOnly? dueDate = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Name = name,
        Status = status,
        DueDate = dueDate,
        CreatedAtUtc = Planned
    };

    /// <summary>A clock stopped at midday on a chosen day, so "today" is whatever a test says it is.</summary>
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateOnly day)
        {
            _now = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
