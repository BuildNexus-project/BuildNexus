using System.Security.Claims;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Controllers;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The US-21 acceptance criteria this service owns, walked over a fake query:
/// a Client sees their active projects, an Architect their assigned ones, and an
/// Admin the system-wide project count.
/// </summary>
/// <remarks>
/// Who may call which endpoint is not decided in the action but by its role
/// attribute, so that is pinned in <c>EndpointRoleDeclarationTests</c>; here the
/// caller is simply the role the route lets through. The other half of each
/// dashboard — design status, progress, payments, revisions, user counts — is
/// another service's data and is tested there.
/// </remarks>
public class ProjectDashboardEndpointTests
{
    private static readonly Guid CallerId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly DateTime Moved = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------- AC-1: Client's projects ----

    [Fact]
    public async Task A_client_gets_their_active_projects_and_how_many_there_are()
    {
        var villa = Row("Beachfront villa", ProjectStatus.Designing);
        var cottage = Row("Hilltop cottage", ProjectStatus.Construction);
        var (controller, _) = ControllerWith(projects: [villa, cottage]);

        var dashboard = await Client(controller);

        Assert.Equal(2, dashboard.ActiveCount);
        Assert.Equal([villa.Id, cottage.Id], dashboard.Projects.Select(p => p.Id));
    }

    [Fact]
    public async Task Each_project_carries_what_a_card_needs()
    {
        var project = Row("Beachfront villa", ProjectStatus.DesignApproved);
        var (controller, _) = ControllerWith(projects: [project]);

        var listed = Assert.Single((await Client(controller)).Projects);

        Assert.Equal(project.Id, listed.Id);
        Assert.Equal("Beachfront villa", listed.Name);
        Assert.Equal("Galle", listed.Location);
        Assert.Equal("DesignApproved", listed.Status);
        Assert.Equal(Moved, listed.UpdatedAt);
    }

    [Fact]
    public async Task A_client_with_nothing_under_way_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _) = ControllerWith();

        var dashboard = await Client(controller);

        Assert.Equal(0, dashboard.ActiveCount);
        Assert.Empty(dashboard.Projects);
    }

    [Fact]
    public async Task The_client_query_is_for_the_caller_named_in_the_token()
    {
        var (controller, repository) = ControllerWith();

        await controller.GetClientDashboard(default);

        Assert.Equal(CallerId, repository.LastClientId);
    }

    [Fact]
    public async Task A_client_token_with_no_usable_subject_is_refused_and_nothing_is_queried()
    {
        var (controller, repository) = ControllerWith(subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetClientDashboard(default));

        Assert.Equal(0, repository.Queries);
    }

    // ---------------------------------------- AC-2: Architect's projects ----

    [Fact]
    public async Task An_architect_gets_their_assigned_projects_and_how_many_there_are()
    {
        var (controller, _) = ControllerWith(
            role: "Architect",
            projects: [Row("Beachfront villa", ProjectStatus.Designing), Row("Lake house", ProjectStatus.Designing)]);

        var dashboard = await Architect(controller);

        Assert.Equal(2, dashboard.AssignedCount);
        Assert.Equal(["Beachfront villa", "Lake house"], dashboard.Projects.Select(p => p.Name));
    }

    [Fact]
    public async Task An_architect_nobody_has_assigned_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _) = ControllerWith(role: "Architect");

        var dashboard = await Architect(controller);

        Assert.Equal(0, dashboard.AssignedCount);
        Assert.Empty(dashboard.Projects);
    }

    [Fact]
    public async Task The_architect_query_is_for_the_caller_named_in_the_token()
    {
        var (controller, repository) = ControllerWith(role: "Architect");

        await controller.GetArchitectDashboard(default);

        Assert.Equal(CallerId, repository.LastArchitectId);
        Assert.Null(repository.LastClientId);
    }

    [Fact]
    public async Task An_architect_token_with_no_usable_subject_is_refused_and_nothing_is_queried()
    {
        var (controller, repository) = ControllerWith(role: "Architect", subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetArchitectDashboard(default));

        Assert.Equal(0, repository.Queries);
    }

    // -------------------------------------------- AC-4: Admin's project count ----

    [Fact]
    public async Task An_admin_gets_the_total_and_the_count_in_each_status()
    {
        var (controller, _) = ControllerWith(
            role: "Admin",
            counts:
            [
                new ProjectStatusCount { Status = ProjectStatus.Pending, Count = 3 },
                new ProjectStatusCount { Status = ProjectStatus.Construction, Count = 2 },
                new ProjectStatusCount { Status = ProjectStatus.Completed, Count = 5 }
            ]);

        var dashboard = await Admin(controller);

        Assert.Equal(10, dashboard.TotalCount);
        Assert.Equal(3, GroupOf(dashboard, "Pending").Count);
        Assert.Equal(2, GroupOf(dashboard, "Construction").Count);
        Assert.Equal(5, GroupOf(dashboard, "Completed").Count);
    }

    [Fact]
    public async Task Every_status_is_reported_in_lifecycle_order_even_those_with_no_projects()
    {
        var (controller, _) = ControllerWith(
            role: "Admin",
            counts: [new ProjectStatusCount { Status = ProjectStatus.Construction, Count = 1 }]);

        var dashboard = await Admin(controller);

        Assert.Equal(
            ["Pending", "Designing", "DesignApproved", "Construction", "Completed", "Cancelled"],
            dashboard.Groups.Select(group => group.Status));
        Assert.Equal(0, GroupOf(dashboard, "Designing").Count);
    }

    [Fact]
    public async Task An_empty_system_is_a_dashboard_of_zeros()
    {
        var (controller, _) = ControllerWith(role: "Admin");

        var dashboard = await Admin(controller);

        Assert.Equal(0, dashboard.TotalCount);
        Assert.Equal(6, dashboard.Groups.Count);
        Assert.All(dashboard.Groups, group => Assert.Equal(0, group.Count));
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ClientProjectsDashboardResponse> Client(DashboardController controller) =>
        Assert.IsType<ClientProjectsDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetClientDashboard(default)).Value);

    private static async Task<ArchitectProjectsDashboardResponse> Architect(DashboardController controller) =>
        Assert.IsType<ArchitectProjectsDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetArchitectDashboard(default)).Value);

    private static async Task<AdminProjectsDashboardResponse> Admin(DashboardController controller) =>
        Assert.IsType<AdminProjectsDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetAdminDashboard(default)).Value);

    private static AdminProjectsDashboardResponse.Group GroupOf(AdminProjectsDashboardResponse dashboard, string status) =>
        Assert.Single(dashboard.Groups, group => group.Status == status);

    private static (DashboardController Controller, FakeProjectDashboardRepository Repository) ControllerWith(
        string role = "Client",
        string? subject = null,
        DashboardProject[]? projects = null,
        ProjectStatusCount[]? counts = null)
    {
        var repository = new FakeProjectDashboardRepository();
        repository.SeedProjects(projects ?? []);
        repository.SeedCounts(counts ?? []);

        var controller = new DashboardController(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The claims a token validated by this service leaves behind:
                    // "sub" and "role" in their short form, not rewritten into
                    // the WS-Federation URIs.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, subject ?? CallerId.ToString()),
                        new Claim(JwtOptions.RoleClaimType, role)
                    ], "TestAuth"))
                }
            }
        };

        return (controller, repository);
    }

    private static DashboardProject Row(string name, ProjectStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Location = "Galle",
        Status = status,
        UpdatedAt = Moved
    };
}
