using System.Security.Claims;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The US-21 acceptance criteria this service owns, walked over a fake repository:
/// a Client sees how far each of their builds has got, and a Project Manager sees
/// the builds under way and the milestones still to finish.
/// </summary>
/// <remarks>
/// Who may call which endpoint is not decided in the action but by its role
/// attribute, so that is pinned in <see cref="EndpointRoleDeclarationTests"/>. The
/// SQL — ownership scoping, the "under way" gate, the outstanding count — is proved
/// in <see cref="ConstructionDashboardRepositoryDatabaseTests"/>. What is under test
/// here is the endpoint's own decisions: whose id it scopes to, what it asks for, and
/// how the rows reach the wire.
/// </remarks>
public class ConstructionDashboardEndpointTests
{
    private static readonly Guid CallerId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid Villa = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid Cottage = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly DateTime Planned = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------ AC-1: Client's progress ----

    [Fact]
    public async Task A_client_sees_the_progress_of_each_of_their_projects()
    {
        var (controller, repository) = ControllerWith("Client");
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
        var (controller, repository) = ControllerWith("Client");
        repository.ClientProgress = [Row(Villa, phase: null, total: 3, completed: 0, inProgress: 0, notStarted: 3, percent: 0m)];

        var project = Assert.Single((await Client(controller)).Projects);

        Assert.Null(project.PhaseStatus);
        Assert.Equal(0m, project.ProgressPercent);
    }

    [Fact]
    public async Task A_client_with_nothing_planned_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _) = ControllerWith("Client");

        Assert.Empty((await Client(controller)).Projects);
    }

    [Fact]
    public async Task The_client_query_is_for_the_caller_named_in_the_token()
    {
        var (controller, repository) = ControllerWith("Client");

        await controller.GetClientDashboard(default);

        Assert.Equal(CallerId, repository.LastClientId);
    }

    [Fact]
    public async Task A_client_token_with_no_usable_subject_is_refused_and_nothing_is_queried()
    {
        var (controller, repository) = ControllerWith("Client", subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetClientDashboard(default));

        Assert.Equal(0, repository.Queries);
    }

    // ---------------------------- AC-3: Project Manager's builds and milestones ----

    [Fact]
    public async Task A_project_manager_sees_the_builds_under_way_and_how_many_there_are()
    {
        var (controller, repository) = ControllerWith("ProjectManager");
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
        var (controller, repository) = ControllerWith("ProjectManager");
        repository.ActiveBuilds =
        [
            Row(Villa, ConstructionPhaseStatus.Started, total: 5, completed: 2, inProgress: 1, notStarted: 2, percent: 40m),
            Row(Cottage, ConstructionPhaseStatus.Completed, total: 3, completed: 3, inProgress: 0, notStarted: 0, percent: 100m)
        ];

        var dashboard = await ProjectManager(controller);

        Assert.Equal([3, 0], dashboard.ActiveBuilds.Select(b => b.OutstandingMilestones));
    }

    [Fact]
    public async Task A_project_manager_sees_the_milestones_still_due_with_the_full_count()
    {
        var (controller, repository) = ControllerWith("ProjectManager");
        var walls = Milestone(Villa, "Walls", MilestoneStatus.InProgress);
        var roof = Milestone(Villa, "Roof", MilestoneStatus.NotStarted);
        repository.Outstanding = new OutstandingMilestones { TotalCount = 14, Items = [walls, roof] };

        var due = (await ProjectManager(controller)).MilestonesDue;

        // The count is everything outstanding, not the length of the short list beside it.
        Assert.Equal(14, due.TotalCount);
        Assert.Equal(["Walls", "Roof"], due.Milestones.Select(m => m.Name));
    }

    [Fact]
    public async Task Each_milestone_due_carries_what_a_row_needs()
    {
        var (controller, repository) = ControllerWith("ProjectManager");
        var walls = Milestone(Villa, "Walls", MilestoneStatus.InProgress);
        repository.Outstanding = new OutstandingMilestones { TotalCount = 1, Items = [walls] };

        var listed = Assert.Single((await ProjectManager(controller)).MilestonesDue.Milestones);

        Assert.Equal(walls.Id, listed.Id);
        Assert.Equal(Villa, listed.ProjectId);
        Assert.Equal("Walls", listed.Name);
        Assert.Equal(MilestoneStatus.InProgress, listed.Status);
        Assert.Equal(Planned, listed.CreatedAt);
    }

    [Fact]
    public async Task The_milestone_list_is_capped_at_the_size_the_endpoint_promises()
    {
        var (controller, repository) = ControllerWith("ProjectManager");

        await controller.GetProjectManagerDashboard(default);

        Assert.Equal(DashboardController.MilestonesDueListSize, repository.LastLimit);
    }

    [Fact]
    public async Task With_nothing_started_a_project_manager_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _) = ControllerWith("ProjectManager");

        var dashboard = await ProjectManager(controller);

        Assert.Equal(0, dashboard.ActiveBuildCount);
        Assert.Empty(dashboard.ActiveBuilds);
        Assert.Equal(0, dashboard.MilestonesDue.TotalCount);
        Assert.Empty(dashboard.MilestonesDue.Milestones);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ClientConstructionDashboardResponse> Client(DashboardController controller) =>
        Assert.IsType<ClientConstructionDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetClientDashboard(default)).Value);

    private static async Task<ProjectManagerConstructionDashboardResponse> ProjectManager(DashboardController controller) =>
        Assert.IsType<ProjectManagerConstructionDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetProjectManagerDashboard(default)).Value);

    private static (DashboardController Controller, FakeConstructionDashboardRepository Repository) ControllerWith(
        string role,
        string? subject = null)
    {
        var repository = new FakeConstructionDashboardRepository();

        var controller = new DashboardController(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The claims a token validated by this service leaves behind: "sub"
                    // and "role" in their short form, not rewritten into the
                    // WS-Federation URIs.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, subject ?? CallerId.ToString()),
                        new Claim("role", role)
                    ], "TestAuth"))
                }
            }
        };

        return (controller, repository);
    }

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

    private static OutstandingMilestone Milestone(Guid projectId, string name, MilestoneStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Name = name,
        Status = status,
        CreatedAtUtc = Planned
    };
}
