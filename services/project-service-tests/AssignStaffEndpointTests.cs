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
/// The US-07 assignment endpoints walked over stand-ins: what the assignment
/// records, when it moves the project on, and how the User Service's answer
/// about the assignee's role is relayed. No MySQL, no User Service.
/// </summary>
public class AssignStaffEndpointTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid ArchitectId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProjectManagerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
    private const string Token = "forwarded.admin.token";

    // -------------------------------------------------- assign architect ----

    [Fact]
    public async Task Assigning_an_architect_to_a_pending_project_moves_it_to_designing()
    {
        var (controller, repository, _) = ControllerFor(ProjectStatus.Pending);

        var detail = await Detail(controller.AssignArchitect(ProjectId, Architect(ArchitectId), default));

        Assert.Equal("Designing", detail.Status);
        Assert.Equal(ArchitectId, detail.AssignedArchitectId);

        Assert.Equal(ArchitectId, repository.AssignedArchitectId);
        Assert.NotNull(repository.Transition);
        Assert.Equal(ProjectStatus.Pending, repository.Transition!.FromStatus);
        Assert.Equal(ProjectStatus.Designing, repository.Transition.ToStatus);
    }

    [Fact]
    public async Task The_pending_transition_is_attributed_to_the_admin_who_made_it()
    {
        var (controller, repository, _) = ControllerFor(ProjectStatus.Pending);

        await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default);

        Assert.Equal(AdminId, repository.Transition!.ChangedByUserId);
        Assert.Equal(PlatformRoles.Admin, repository.Transition.ChangedByRole);
    }

    [Fact]
    public async Task The_pending_move_raises_one_ProjectUpdated_event()
    {
        var (controller, repository, _) = ControllerFor(ProjectStatus.Pending);

        await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default);

        var raised = Assert.Single(repository.RaisedEvents);
        Assert.Equal(ProjectEventTypes.ProjectUpdated, raised.EventType);
    }

    [Fact]
    public async Task Assigning_an_architect_to_a_project_past_pending_only_sets_the_slot()
    {
        var (controller, repository, _) = ControllerFor(ProjectStatus.Designing);

        var detail = await Detail(controller.AssignArchitect(ProjectId, Architect(ArchitectId), default));

        Assert.Equal("Designing", detail.Status);
        Assert.Equal(ArchitectId, detail.AssignedArchitectId);
        Assert.Null(repository.Transition);
        Assert.Empty(repository.RaisedEvents);
    }

    [Fact]
    public async Task An_unknown_account_is_refused_as_a_field_error()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.Pending);
        directory.Result = UserLookup.NotFound;

        var problem = await ValidationProblemFor(controller.AssignArchitect(ProjectId, Architect(ArchitectId), default));

        Assert.Contains(nameof(AssignArchitectRequest.ArchitectId), problem.Errors.Keys);
        Assert.Null(repository.AssignedArchitectId);
    }

    [Fact]
    public async Task An_account_that_is_not_an_architect_is_refused()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.Pending);
        directory.Result = UserLookup.Found(PlatformRoles.ProjectManager);

        var problem = await ValidationProblemFor(controller.AssignArchitect(ProjectId, Architect(ArchitectId), default));

        Assert.Contains(nameof(AssignArchitectRequest.ArchitectId), problem.Errors.Keys);
        Assert.Null(repository.AssignedArchitectId);
    }

    [Fact]
    public async Task A_user_service_that_cannot_be_reached_is_a_502_and_writes_nothing()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.Pending);
        directory.Result = UserLookup.Unavailable;

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            StatusOf(await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default)));
        Assert.Null(repository.AssignedArchitectId);
    }

    [Fact]
    public async Task The_admins_token_and_the_assignee_id_are_forwarded_to_the_user_service()
    {
        var (controller, _, directory) = ControllerFor(ProjectStatus.Pending);

        await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default);

        Assert.Equal(ArchitectId, directory.LastUserId);
        Assert.Equal(Token, directory.LastToken);
    }

    [Fact]
    public async Task A_project_that_does_not_exist_is_a_404_and_never_asks_the_user_service()
    {
        var (controller, _, directory) = ControllerFor(ProjectStatus.Pending, seedProject: false);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            StatusOf(await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default)));
        Assert.Equal(0, directory.Calls);
    }

    [Fact]
    public async Task A_project_that_moved_off_pending_under_the_caller_is_a_409()
    {
        var (controller, repository, _) = ControllerFor(ProjectStatus.Pending);
        repository.AssignSucceeds = false;

        Assert.Equal(
            StatusCodes.Status409Conflict,
            StatusOf(await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default)));
    }

    [Fact]
    public async Task Refuses_a_token_with_no_usable_subject()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.Pending, subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.AssignArchitect(ProjectId, Architect(ArchitectId), default));
        Assert.Equal(0, directory.Calls);
        Assert.Null(repository.AssignedArchitectId);
    }

    // -------------------------------------------- assign project manager ----

    [Fact]
    public async Task Assigning_a_project_manager_sets_the_slot_without_moving_the_project()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.DesignApproved);
        directory.Result = UserLookup.Found(PlatformRoles.ProjectManager);

        var detail = await Detail(controller.AssignProjectManager(ProjectId, ProjectManager(ProjectManagerId), default));

        Assert.Equal("DesignApproved", detail.Status);
        Assert.Equal(ProjectManagerId, detail.AssignedProjectManagerId);
        Assert.Equal(ProjectManagerId, repository.AssignedProjectManagerId);
        Assert.Empty(repository.RaisedEvents);
    }

    [Fact]
    public async Task An_account_that_is_not_a_project_manager_is_refused()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.DesignApproved);
        directory.Result = UserLookup.Found(PlatformRoles.Architect);

        var problem = await ValidationProblemFor(
            controller.AssignProjectManager(ProjectId, ProjectManager(ProjectManagerId), default));

        Assert.Contains(nameof(AssignProjectManagerRequest.ProjectManagerId), problem.Errors.Keys);
        Assert.Null(repository.AssignedProjectManagerId);
    }

    [Fact]
    public async Task A_project_manager_assignment_on_a_project_that_vanished_is_a_409()
    {
        var (controller, repository, directory) = ControllerFor(ProjectStatus.DesignApproved);
        directory.Result = UserLookup.Found(PlatformRoles.ProjectManager);
        repository.AssignSucceeds = false;

        Assert.Equal(
            StatusCodes.Status409Conflict,
            StatusOf(await controller.AssignProjectManager(ProjectId, ProjectManager(ProjectManagerId), default)));
    }

    // ------------------------------------------------------------- setup ----

    private static async Task<ProjectDetailResponse> Detail(Task<IActionResult> action) =>
        Assert.IsType<ProjectDetailResponse>(Assert.IsType<OkObjectResult>(await action).Value);

    private static int? StatusOf(IActionResult result) =>
        Assert.IsAssignableFrom<ObjectResult>(result).StatusCode;

    private static async Task<ValidationProblemDetails> ValidationProblemFor(Task<IActionResult> action) =>
        Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(await action).Value);

    private static AssignArchitectRequest Architect(Guid id) => new() { ArchitectId = id };

    private static AssignProjectManagerRequest ProjectManager(Guid id) => new() { ProjectManagerId = id };

    private static (ProjectsController Controller, RecordingProjectRepository Repository, FakeUserDirectoryClient Directory)
        ControllerFor(ProjectStatus status, bool seedProject = true, string? subject = null)
    {
        var repository = new RecordingProjectRepository();

        if (seedProject)
        {
            repository.Seed(new Project
            {
                Id = ProjectId,
                ClientId = ClientId,
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
            });
        }

        var directory = new FakeUserDirectoryClient { Result = UserLookup.Found(PlatformRoles.Architect) };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject ?? AdminId.ToString()),
                new Claim(JwtOptions.RoleClaimType, PlatformRoles.Admin)
            ], "TestAuth"))
        };
        httpContext.Request.Headers.Authorization = $"Bearer {Token}";

        var controller = new ProjectsController(
            repository,
            new FakeOutboxRepository(),
            directory,
            NullLogger<ProjectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, repository, directory);
    }

    /// <summary>
    /// Holds one project in memory and records what the assign actions asked it
    /// to write. The SQL those methods run is covered against real MySQL in
    /// <see cref="ProjectRepositoryDatabaseTests"/>.
    /// </summary>
    private sealed class RecordingProjectRepository : IProjectRepository
    {
        private Project? _project;

        public Guid? AssignedArchitectId { get; private set; }

        public Guid? AssignedProjectManagerId { get; private set; }

        public ProjectStatusChange? Transition { get; private set; }

        public IReadOnlyList<OutboxEvent> RaisedEvents { get; private set; } = [];

        /// <summary>Set false to stand in for the guarded UPDATE matching no row.</summary>
        public bool AssignSucceeds { get; set; } = true;

        public void Seed(Project project) => _project = project;

        public Task<Project?> GetByIdAsync(Guid id) =>
            Task.FromResult(_project is not null && _project.Id == id ? _project : null);

        public Task<bool> AssignArchitectAsync(
            Guid projectId,
            Guid architectId,
            ProjectStatusChange? transition,
            IReadOnlyList<OutboxEvent> outboxEvents,
            DateTime updatedAtUtc)
        {
            if (!AssignSucceeds)
            {
                return Task.FromResult(false);
            }

            AssignedArchitectId = architectId;
            Transition = transition;
            RaisedEvents = outboxEvents;

            return Task.FromResult(true);
        }

        public Task<bool> AssignProjectManagerAsync(Guid projectId, Guid projectManagerId, DateTime updatedAtUtc)
        {
            if (!AssignSucceeds)
            {
                return Task.FromResult(false);
            }

            AssignedProjectManagerId = projectManagerId;

            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            Task.FromResult<IReadOnlyList<ProjectStatusChange>>([]);

        // Not exercised by these tests.
        public Task InsertAsync(Project project, ProjectStatusChange creation, IReadOnlyList<OutboxEvent> outboxEvents) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Project>> ListAllAsync() =>
            Task.FromResult<IReadOnlyList<Project>>([]);

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) =>
            Task.FromResult<IReadOnlyList<Project>>([]);

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents) =>
            Task.FromResult(false);
    }
}
