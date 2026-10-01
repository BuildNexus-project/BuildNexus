using BuildNexus.DesignService.Contracts;
using BuildNexus.DesignService.Controllers;
using BuildNexus.DesignService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The US-21 acceptance criteria this service owns, walked over fake collaborators:
/// a Client sees where the design stands on each active project, and an Architect
/// sees the revisions they still owe.
/// </summary>
/// <remarks>
/// Who may call which endpoint is not decided in the action but by its role
/// attribute, so that is pinned in <c>EndpointRoleDeclarationTests</c>. The SQL
/// (latest version, review-state counts) is proved in
/// <c>DesignDashboardRepositoryDatabaseTests</c>. What is under test here is the
/// endpoint's own decisions: which projects it asks about, that it forwards the
/// caller's token, and that a Project Service it cannot reach is not turned into
/// an empty dashboard.
/// </remarks>
public class DesignDashboardEndpointTests
{
    private const string Token = "caller-token";

    private static readonly Guid Villa = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid Cottage = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly Guid Bungalow = Guid.Parse("33333333-0000-4000-8000-000000000003");
    private static readonly DateTime Asked = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    // -------------------------------------------- AC-1: Client's design status ----

    [Fact]
    public async Task A_client_sees_the_design_state_of_each_active_project()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"), (Cottage, "DesignApproved"));
        repository.Tallies =
        [
            Tally(Villa, awaiting: 1, revision: 0, approved: 1),
            Tally(Cottage, awaiting: 0, revision: 0, approved: 2)
        ];

        var dashboard = await Client(controller);

        Assert.Equal(["AwaitingReview", "Approved"], dashboard.Projects.Select(p => p.State));
        Assert.Equal([Villa, Cottage], dashboard.Projects.Select(p => p.ProjectId));
    }

    [Fact]
    public async Task Carries_the_document_counts_behind_each_state()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"));
        repository.Tallies = [Tally(Villa, awaiting: 2, revision: 1, approved: 3)];

        var status = Assert.Single((await Client(controller)).Projects);

        Assert.Equal(6, status.DocumentCount);
        Assert.Equal(2, status.AwaitingReviewCount);
        Assert.Equal(1, status.RevisionRequestedCount);
        Assert.Equal(3, status.ApprovedCount);
    }

    [Fact]
    public async Task A_project_nothing_has_been_uploaded_for_is_reported_as_no_design_not_left_out()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Pending"), (Cottage, "Designing"));
        repository.Tallies = [Tally(Cottage, awaiting: 1, revision: 0, approved: 0)];

        var dashboard = await Client(controller);

        Assert.Equal([Villa, Cottage], dashboard.Projects.Select(p => p.ProjectId));
        var villa = dashboard.Projects[0];
        Assert.Equal("NoDesign", villa.State);
        Assert.Equal(0, villa.DocumentCount);
    }

    [Fact]
    public async Task Keeps_the_order_the_project_service_listed_the_projects_in()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Bungalow, "Designing"), (Villa, "Designing"), (Cottage, "Designing"));
        // The tally rows come back in whatever order MySQL likes.
        repository.Tallies =
        [
            Tally(Villa, 1, 0, 0),
            Tally(Cottage, 1, 0, 0),
            Tally(Bungalow, 1, 0, 0)
        ];

        var dashboard = await Client(controller);

        Assert.Equal([Bungalow, Villa, Cottage], dashboard.Projects.Select(p => p.ProjectId));
    }

    [Fact]
    public async Task Only_the_clients_active_projects_are_asked_about()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"), (Cottage, "Completed"), (Bungalow, "Cancelled"));

        var dashboard = await Client(controller);

        Assert.Equal([Villa], repository.LastProjectIds);
        Assert.Equal([Villa], dashboard.Projects.Select(p => p.ProjectId));
    }

    [Fact]
    public async Task A_client_with_no_projects_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _, projects) = ControllerWith();
        projects.Result = Visible();

        Assert.Empty((await Client(controller)).Projects);
    }

    [Fact]
    public async Task The_clients_own_token_is_what_the_project_service_is_asked_with()
    {
        var (controller, _, projects) = ControllerWith();

        await controller.GetClientDashboard(default);

        Assert.Equal(Token, projects.LastToken);
    }

    [Fact]
    public async Task A_project_service_that_cannot_answer_is_a_502_not_an_empty_dashboard()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = VisibleProjects.Unavailable;

        var result = await controller.GetClientDashboard(default);

        Assert.Equal(StatusCodes.Status502BadGateway, StatusOf(result));
        Assert.Equal(0, repository.Queries);
    }

    [Fact]
    public async Task A_request_carrying_no_bearer_token_is_refused_before_anything_is_asked()
    {
        var (controller, repository, projects) = ControllerWith(authorization: null);

        Assert.IsType<UnauthorizedResult>(await controller.GetClientDashboard(default));

        Assert.Equal(0, projects.Calls);
        Assert.Equal(0, repository.Queries);
    }

    // ---------------------------------------- AC-2: Architect's revisions ----

    [Fact]
    public async Task An_architect_sees_the_revisions_they_still_owe_and_how_many_there_are()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"));
        repository.Revisions =
        [
            Revision(Villa, "Elevations", version: 2, comment: "Taller windows"),
            Revision(Villa, "GroundFloorPlan", version: 1, comment: "Move the stairs")
        ];

        var dashboard = await Architect(controller);

        Assert.Equal(2, dashboard.PendingRevisionCount);
        Assert.Equal(["Elevations", "GroundFloorPlan"], dashboard.Revisions.Select(r => r.DocumentName));
    }

    [Fact]
    public async Task Each_revision_carries_what_the_architect_needs_to_act_on_it()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"));
        var revision = Revision(Villa, "Elevations", version: 2, comment: "Taller windows");
        repository.Revisions = [revision];

        var listed = Assert.Single((await Architect(controller)).Revisions);

        Assert.Equal(Villa, listed.ProjectId);
        Assert.Equal(revision.DocumentId, listed.DocumentId);
        Assert.Equal("Elevations", listed.DocumentName);
        Assert.Equal(2, listed.VersionNumber);
        Assert.Equal("Taller windows", listed.ReviewComment);
        Assert.Equal(Asked, listed.RequestedAt);
    }

    [Fact]
    public async Task An_architect_with_nothing_to_redo_gets_an_empty_dashboard_not_an_error()
    {
        var (controller, _, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"));

        var dashboard = await Architect(controller);

        Assert.Equal(0, dashboard.PendingRevisionCount);
        Assert.Empty(dashboard.Revisions);
    }

    [Fact]
    public async Task Only_the_projects_the_project_service_says_the_architect_is_on_are_asked_about()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = Visible((Villa, "Designing"), (Cottage, "Completed"));

        await controller.GetArchitectDashboard(default);

        Assert.Equal([Villa], repository.LastProjectIds);
    }

    [Fact]
    public async Task The_architects_own_token_is_what_the_project_service_is_asked_with()
    {
        var (controller, _, projects) = ControllerWith();

        await controller.GetArchitectDashboard(default);

        Assert.Equal(Token, projects.LastToken);
    }

    [Fact]
    public async Task An_architect_dashboard_is_a_502_when_the_project_service_cannot_answer()
    {
        var (controller, repository, projects) = ControllerWith();
        projects.Result = VisibleProjects.Unavailable;

        var result = await controller.GetArchitectDashboard(default);

        Assert.Equal(StatusCodes.Status502BadGateway, StatusOf(result));
        Assert.Equal(0, repository.Queries);
    }

    [Fact]
    public async Task An_architect_request_carrying_no_bearer_token_is_refused()
    {
        var (controller, repository, projects) = ControllerWith(authorization: "");

        Assert.IsType<UnauthorizedResult>(await controller.GetArchitectDashboard(default));

        Assert.Equal(0, projects.Calls);
        Assert.Equal(0, repository.Queries);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ClientDesignDashboardResponse> Client(DashboardController controller) =>
        Assert.IsType<ClientDesignDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetClientDashboard(default)).Value);

    private static async Task<ArchitectDesignDashboardResponse> Architect(DashboardController controller) =>
        Assert.IsType<ArchitectDesignDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetArchitectDashboard(default)).Value);

    private static int? StatusOf(IActionResult result) => Assert.IsAssignableFrom<ObjectResult>(result).StatusCode;

    private static (DashboardController Controller, FakeDesignDashboardRepository Repository, FakeProjectDirectoryClient Projects)
        ControllerWith(string? authorization = $"Bearer {Token}")
    {
        var repository = new FakeDesignDashboardRepository();
        var projects = new FakeProjectDirectoryClient();

        var httpContext = new DefaultHttpContext();

        if (authorization is not null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        var controller = new DashboardController(repository, projects)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, repository, projects);
    }

    private static VisibleProjects Visible(params (Guid Id, string Status)[] projects) =>
        VisibleProjects.Available(projects.Select(p => new VisibleProject(p.Id, p.Status)).ToList());

    private static ProjectDesignTally Tally(Guid projectId, int awaiting, int revision, int approved) => new()
    {
        ProjectId = projectId,
        DocumentCount = awaiting + revision + approved,
        AwaitingReviewCount = awaiting,
        RevisionRequestedCount = revision,
        ApprovedCount = approved
    };

    private static PendingRevision Revision(Guid projectId, string name, int version, string comment) => new()
    {
        ProjectId = projectId,
        DocumentId = Guid.NewGuid(),
        DocumentName = name,
        VersionNumber = version,
        ReviewComment = comment,
        RequestedAtUtc = Asked
    };
}
