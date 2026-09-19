using System.Security.Claims;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Controllers;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// US-08 over stand-ins: who may cancel a project and when, what the
/// cancellation records, and how a cancelled project drops off the active
/// list. No MySQL, no broker.
/// </summary>
public class CancelProjectEndpointTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid OwnerId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid OutsiderId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ProjectStatus.Pending)]
    [InlineData(ProjectStatus.Designing)]
    [InlineData(ProjectStatus.DesignApproved)]
    public async Task Cancelling_moves_a_pre_construction_project_to_the_terminal_cancelled_state(
        ProjectStatus from)
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client, status: from);

        var detail = await Detail(controller.CancelProject(ProjectId, Reason("Client secured a different plot")));

        Assert.Equal("Cancelled", detail.Status);
        Assert.Empty(detail.AllowedNextStatuses);

        Assert.NotNull(repository.LastChange);
        Assert.Equal(from, repository.LastChange!.FromStatus);
        Assert.Equal(ProjectStatus.Cancelled, repository.LastChange.ToStatus);
    }

    [Fact]
    public async Task The_reason_is_trimmed_and_recorded_on_the_cancelling_transition()
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client);

        await controller.CancelProject(ProjectId, Reason("  Budget fell through  "));

        Assert.Equal("Budget fell through", repository.LastChange!.Note);
    }

    [Fact]
    public async Task The_cancellation_is_attributed_to_the_caller()
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client);

        await controller.CancelProject(ProjectId, Reason("No longer needed"));

        Assert.Equal(OwnerId, repository.LastChange!.ChangedByUserId);
        Assert.Equal(PlatformRoles.Client, repository.LastChange.ChangedByRole);
    }

    [Fact]
    public async Task Cancelling_raises_one_ProjectUpdated_event()
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client);

        await controller.CancelProject(ProjectId, Reason("No longer needed"));

        var raised = Assert.Single(repository.LastEvents);
        Assert.Equal(ProjectEventTypes.ProjectUpdated, raised.EventType);
    }

    [Fact]
    public async Task An_admin_can_cancel_a_project_they_do_not_own()
    {
        var (controller, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        var detail = await Detail(controller.CancelProject(ProjectId, Reason("Duplicate submission")));

        Assert.Equal("Cancelled", detail.Status);
    }

    [Fact]
    public async Task A_client_who_does_not_own_the_project_is_refused()
    {
        var (controller, repository) = ControllerFor(OutsiderId, PlatformRoles.Client);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(await controller.CancelProject(ProjectId, Reason("not mine"))));
        Assert.Null(repository.LastChange);
    }

    [Theory]
    [InlineData(ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Completed)]
    public async Task A_project_at_or_past_construction_cannot_be_cancelled(ProjectStatus status)
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client, status: status);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            StatusOf(await controller.CancelProject(ProjectId, Reason("too late"))));
        Assert.Null(repository.LastChange);
    }

    [Fact]
    public async Task An_already_cancelled_project_says_so()
    {
        var (controller, _) = ControllerFor(OwnerId, PlatformRoles.Client, status: ProjectStatus.Cancelled);

        var problem = Assert.IsAssignableFrom<ObjectResult>(
            await controller.CancelProject(ProjectId, Reason("again")));

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Contains("already been cancelled", ((ProblemDetails)problem.Value!).Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_a_field_error_and_writes_nothing(string reason)
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client);

        var problem = Assert.IsType<ValidationProblemDetails>(
            Assert.IsAssignableFrom<ObjectResult>(await controller.CancelProject(ProjectId, Reason(reason))).Value);

        Assert.Contains(nameof(CancelProjectRequest.Reason), problem.Errors.Keys);
        Assert.Null(repository.LastChange);
    }

    [Fact]
    public async Task A_project_that_does_not_exist_is_a_404()
    {
        var (controller, _) = ControllerFor(OwnerId, PlatformRoles.Client, seedProject: false);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            StatusOf(await controller.CancelProject(ProjectId, Reason("gone"))));
    }

    [Fact]
    public async Task A_project_that_moved_under_the_caller_is_a_409()
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client);
        repository.UpdateSucceeds = false;

        Assert.Equal(
            StatusCodes.Status409Conflict,
            StatusOf(await controller.CancelProject(ProjectId, Reason("reload"))));
    }

    [Fact]
    public async Task Refuses_a_token_with_no_usable_subject()
    {
        var (controller, repository) = ControllerFor(OwnerId, PlatformRoles.Client, subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.CancelProject(ProjectId, Reason("x")));
        Assert.Null(repository.LastChange);
    }

    // ------------------------------------------- the active list (AC3) ----

    [Fact]
    public async Task A_cancelled_project_is_left_out_of_the_list_by_default()
    {
        var (controller, _) = ControllerFor(AdminId, PlatformRoles.Admin, alsoSeedACancelledProject: true);

        var summaries = Assert.IsType<List<ProjectSummaryResponse>>(
            Assert.IsType<OkObjectResult>(await controller.ListProjects()).Value);

        Assert.DoesNotContain(summaries, summary => summary.Status == "Cancelled");
        Assert.Single(summaries);
    }

    [Fact]
    public async Task Include_cancelled_brings_it_back()
    {
        var (controller, _) = ControllerFor(AdminId, PlatformRoles.Admin, alsoSeedACancelledProject: true);

        var summaries = Assert.IsType<List<ProjectSummaryResponse>>(
            Assert.IsType<OkObjectResult>(await controller.ListProjects(includeCancelled: true)).Value);

        Assert.Contains(summaries, summary => summary.Status == "Cancelled");
        Assert.Equal(2, summaries.Count);
    }

    // ------------------------------------------------------------- setup ----

    private static async Task<ProjectDetailResponse> Detail(Task<IActionResult> action) =>
        Assert.IsType<ProjectDetailResponse>(Assert.IsType<OkObjectResult>(await action).Value);

    private static int? StatusOf(IActionResult result) =>
        Assert.IsAssignableFrom<ObjectResult>(result).StatusCode;

    private static CancelProjectRequest Reason(string reason) => new() { Reason = reason };

    private static (ProjectsController Controller, RecordingProjectRepository Repository) ControllerFor(
        Guid userId,
        string role,
        ProjectStatus status = ProjectStatus.Designing,
        bool seedProject = true,
        bool alsoSeedACancelledProject = false,
        string? subject = null)
    {
        var repository = new RecordingProjectRepository();

        if (seedProject)
        {
            repository.Seed(SeedProject(ProjectId, status));
        }

        if (alsoSeedACancelledProject)
        {
            repository.Seed(SeedProject(Guid.Parse("11112222-3333-4444-8555-666677778888"), ProjectStatus.Cancelled));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject ?? userId.ToString()),
                new Claim(JwtOptions.RoleClaimType, role)
            ], "TestAuth"))
        };

        var controller = new ProjectsController(
            repository,
            new FakeOutboxRepository(),
            new FakeUserDirectoryClient(),
            NullLogger<ProjectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, repository);
    }

    private static Project SeedProject(Guid id, ProjectStatus status) => new()
    {
        Id = id,
        ClientId = OwnerId,
        Name = "Beachfront villa",
        Location = "Galle",
        LandSizePerches = 25.5m,
        Budget = 18_500_000m,
        Floors = 2,
        Bedrooms = 4,
        Bathrooms = 3,
        GarageSpaces = 2,
        Status = status,
        CreatedAt = CreatedAt,
        UpdatedAt = CreatedAt
    };

    /// <summary>
    /// Holds projects in memory and records the move a cancellation asked it to
    /// write. The SQL is covered against real MySQL in
    /// <see cref="ProjectRepositoryDatabaseTests"/>.
    /// </summary>
    private sealed class RecordingProjectRepository : IProjectRepository
    {
        private readonly List<Project> _projects = [];

        public ProjectStatusChange? LastChange { get; private set; }

        public IReadOnlyList<OutboxEvent> LastEvents { get; private set; } = [];

        /// <summary>Set false to stand in for the guarded UPDATE matching no row.</summary>
        public bool UpdateSucceeds { get; set; } = true;

        public void Seed(Project project) => _projects.Add(project);

        public Task<Project?> GetByIdAsync(Guid id) =>
            Task.FromResult(_projects.SingleOrDefault(project => project.Id == id));

        public Task<IReadOnlyList<Project>> ListAllAsync() =>
            Task.FromResult<IReadOnlyList<Project>>(_projects);

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) =>
            Task.FromResult<IReadOnlyList<Project>>(_projects);

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            Task.FromResult<IReadOnlyList<ProjectStatusChange>>(LastChange is null ? [] : [LastChange]);

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents)
        {
            if (!UpdateSucceeds)
            {
                return Task.FromResult(false);
            }

            LastChange = change;
            LastEvents = outboxEvents;

            var project = _projects.Single(candidate => candidate.Id == change.ProjectId);
            project.Status = change.ToStatus;
            project.UpdatedAt = updatedAtUtc;

            return Task.FromResult(true);
        }

        // Not exercised by these tests.
        public Task InsertAsync(Project project, ProjectStatusChange creation, IReadOnlyList<OutboxEvent> outboxEvents) =>
            Task.CompletedTask;

        public Task<bool> AssignArchitectAsync(
            Guid projectId,
            Guid architectId,
            ProjectStatusChange? transition,
            IReadOnlyList<OutboxEvent> outboxEvents,
            DateTime updatedAtUtc) =>
            Task.FromResult(false);

        public Task<bool> AssignProjectManagerAsync(Guid projectId, Guid projectManagerId, DateTime updatedAtUtc) =>
            Task.FromResult(false);
    }
}
