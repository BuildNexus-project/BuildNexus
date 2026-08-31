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
/// The US-06 acceptance criteria walked over a fake repository: what the view
/// returns, who is refused it, and what a status change writes.
/// </summary>
/// <remarks>
/// The repository is a stand-in holding projects and history in memory, so
/// these need neither MySQL nor a broker. What is under test is the actions'
/// own decisions — which rows they ask for, who they refuse, what they record
/// and what they answer with — not the SQL, which cannot be exercised without
/// the real thing running.
/// </remarks>
public class ProjectStatusEndpointTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid ArchitectId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProjectManagerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OutsiderId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------ viewing ----

    [Fact]
    public async Task Shows_the_requirements_status_assignments_and_creation_date()
    {
        // The first AC bullet, field by field.
        var (controller, _) = ControllerFor(ClientId, PlatformRoles.Client);

        var detail = await Detail(controller.GetProject(ProjectId));

        Assert.Equal("Beachfront villa", detail.Name);
        Assert.Equal("Galle", detail.Location);
        Assert.Equal(25.5m, detail.LandSizePerches);
        Assert.Equal(18_500_000m, detail.Budget);
        Assert.Equal(2, detail.Floors);
        Assert.Equal(4, detail.Bedrooms);
        Assert.Equal(3, detail.Bathrooms);
        Assert.Equal(2, detail.GarageSpaces);
        Assert.Equal("Solar hot water", detail.OtherRequirements);
        Assert.Equal("Designing", detail.Status);
        Assert.Equal(ArchitectId, detail.AssignedArchitectId);
        Assert.Equal(ProjectManagerId, detail.AssignedProjectManagerId);
        Assert.Equal(CreatedAt, detail.CreatedAt);
    }

    [Fact]
    public async Task Shows_the_full_status_history_in_chronological_order()
    {
        var (controller, _) = ControllerFor(ClientId, PlatformRoles.Client);

        var detail = await Detail(controller.GetProject(ProjectId));

        // Oldest first, and starting at the creation — a history that begins
        // halfway through is not the full record the story asks for.
        Assert.Equal(2, detail.StatusHistory.Count);
        Assert.Null(detail.StatusHistory[0].FromStatus);
        Assert.Equal("Pending", detail.StatusHistory[0].ToStatus);
        Assert.Equal("Pending", detail.StatusHistory[1].FromStatus);
        Assert.Equal("Designing", detail.StatusHistory[1].ToStatus);
        Assert.True(detail.StatusHistory[0].ChangedAt <= detail.StatusHistory[1].ChangedAt);
    }

    [Fact]
    public async Task Tells_the_caller_which_statuses_the_project_may_move_to()
    {
        // So the screen offers only moves the service will accept, rather than
        // reimplementing the transition table and drifting from it.
        var (controller, _) = ControllerFor(ClientId, PlatformRoles.Client);

        var detail = await Detail(controller.GetProject(ProjectId));

        Assert.Equal(["DesignApproved"], detail.AllowedNextStatuses);
    }

    [Fact]
    public async Task Offers_nothing_further_on_a_completed_project()
    {
        var (controller, repository) = ControllerFor(ClientId, PlatformRoles.Client);
        repository.Single.Status = ProjectStatus.Completed;

        var detail = await Detail(controller.GetProject(ProjectId));

        Assert.Empty(detail.AllowedNextStatuses);
    }

    [Theory]
    [InlineData(nameof(ArchitectId))]
    [InlineData(nameof(ProjectManagerId))]
    public async Task Lets_the_assigned_staff_open_the_project(string who)
    {
        var userId = who == nameof(ArchitectId) ? ArchitectId : ProjectManagerId;
        var role = who == nameof(ArchitectId) ? PlatformRoles.Architect : PlatformRoles.ProjectManager;
        var (controller, _) = ControllerFor(userId, role);

        Assert.IsType<OkObjectResult>(await controller.GetProject(ProjectId));
    }

    [Fact]
    public async Task Lets_an_admin_open_any_project()
    {
        var (controller, _) = ControllerFor(OutsiderId, PlatformRoles.Admin);

        Assert.IsType<OkObjectResult>(await controller.GetProject(ProjectId));
    }

    [Theory]
    [InlineData(PlatformRoles.Client)]
    [InlineData(PlatformRoles.Architect)]
    [InlineData(PlatformRoles.ProjectManager)]
    public async Task Refuses_somebody_who_is_not_on_the_project(string role)
    {
        // The second AC bullet. Holding the right role is not the same as being
        // on this project.
        var (controller, _) = ControllerFor(OutsiderId, role);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(await controller.GetProject(ProjectId)));
    }

    [Fact]
    public async Task Answers_404_for_a_project_that_does_not_exist()
    {
        var (controller, _) = ControllerFor(ClientId, PlatformRoles.Client);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(await controller.GetProject(Guid.NewGuid())));
    }

    [Fact]
    public async Task Refuses_a_token_that_carries_no_usable_subject_when_viewing()
    {
        var (controller, _) = ControllerFor(ClientId, PlatformRoles.Client, subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetProject(ProjectId));
    }

    // ------------------------------------------------------------ listing ----

    [Fact]
    public async Task Lists_only_the_projects_the_caller_is_party_to()
    {
        var (controller, repository) = ControllerFor(ClientId, PlatformRoles.Client);

        var summaries = Assert.IsType<List<ProjectSummaryResponse>>(
            Assert.IsType<OkObjectResult>(await controller.ListProjects()).Value);

        // Scoped by the caller's own id, so nobody is shown a project they would
        // be refused when they clicked it.
        Assert.Equal(ClientId, repository.ListedForUser);
        Assert.False(repository.ListedAll);
        Assert.Equal(ProjectId, Assert.Single(summaries).Id);
    }

    [Fact]
    public async Task Lists_every_project_for_an_admin()
    {
        var (controller, repository) = ControllerFor(OutsiderId, PlatformRoles.Admin);

        Assert.IsType<OkObjectResult>(await controller.ListProjects());

        Assert.True(repository.ListedAll);
        Assert.Null(repository.ListedForUser);
    }

    // ----------------------------------------------------- changing status ----

    [Fact]
    public async Task Moves_the_project_along_a_valid_transition()
    {
        // The third AC bullet, from the allowed side.
        var (controller, repository) = ControllerFor(ArchitectId, PlatformRoles.Architect);

        var detail = await Detail(controller.UpdateProjectStatus(ProjectId, Request("DesignApproved")));

        Assert.Equal(ProjectStatus.DesignApproved, repository.Single.Status);
        Assert.Equal("DesignApproved", detail.Status);
        Assert.Equal(["Construction"], detail.AllowedNextStatuses);
    }

    [Fact]
    public async Task Writes_a_history_record_for_the_change()
    {
        // "Each change writes a history record" — with where it came from, where
        // it went, and who did it.
        var (controller, repository) = ControllerFor(ProjectManagerId, PlatformRoles.ProjectManager);

        await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        var written = repository.UpdatedWith!;
        Assert.Equal(ProjectId, written.ProjectId);
        Assert.Equal(ProjectStatus.Designing, written.FromStatus);
        Assert.Equal(ProjectStatus.DesignApproved, written.ToStatus);
        Assert.NotEqual(Guid.Empty, written.Id);
        Assert.Equal(DateTimeKind.Utc, written.ChangedAt.Kind);
    }

    [Fact]
    public async Task Records_the_caller_from_their_own_token_and_not_the_payload()
    {
        // An audit trail that could be attributed to somebody else is not worth
        // keeping — and there is no field on the request to try it with.
        var (controller, repository) = ControllerFor(ProjectManagerId, PlatformRoles.ProjectManager);

        await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.Equal(ProjectManagerId, repository.UpdatedWith!.ChangedByUserId);
        Assert.Equal(PlatformRoles.ProjectManager, repository.UpdatedWith.ChangedByRole);
    }

    [Fact]
    public async Task Returns_the_new_entry_in_the_history_it_answers_with()
    {
        var (controller, _) = ControllerFor(ArchitectId, PlatformRoles.Architect);

        var detail = await Detail(controller.UpdateProjectStatus(ProjectId, Request("DesignApproved")));

        Assert.Equal(3, detail.StatusHistory.Count);
        Assert.Equal("Designing", detail.StatusHistory[2].FromStatus);
        Assert.Equal("DesignApproved", detail.StatusHistory[2].ToStatus);
        Assert.Equal(ArchitectId, detail.StatusHistory[2].ChangedByUserId);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Designing")]
    [InlineData("Construction")]
    [InlineData("Completed")]
    public async Task Refuses_a_move_the_lifecycle_does_not_allow(string target)
    {
        // From Designing, everything but DesignApproved is backwards, a skipped
        // stage, or no change at all.
        var (controller, repository) = ControllerFor(ArchitectId, PlatformRoles.Architect);

        var result = await controller.UpdateProjectStatus(ProjectId, Request(target));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(ProjectStatus.Designing, repository.Single.Status);
        Assert.Null(repository.UpdatedWith);
    }

    [Fact]
    public async Task Refuses_to_move_a_completed_project_anywhere()
    {
        var (controller, repository) = ControllerFor(ArchitectId, PlatformRoles.Architect);
        repository.Single.Status = ProjectStatus.Completed;

        var result = await controller.UpdateProjectStatus(ProjectId, Request("Construction"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Null(repository.UpdatedWith);
    }

    [Fact]
    public async Task Says_where_the_project_actually_is_when_it_refuses_a_move()
    {
        // "Invalid transition" on its own tells the caller nothing they can act
        // on, and this message is what the UI shows them.
        var (controller, _) = ControllerFor(ArchitectId, PlatformRoles.Architect);

        var problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<ObjectResult>(await controller.UpdateProjectStatus(ProjectId, Request("Completed"))).Value);

        Assert.Contains("Designing", problem.Detail);
        Assert.Contains("DesignApproved", problem.Detail);
    }

    [Fact]
    public async Task Refuses_staff_who_are_not_assigned_to_the_project()
    {
        var (controller, repository) = ControllerFor(OutsiderId, PlatformRoles.Architect);

        var result = await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        Assert.Null(repository.UpdatedWith);
    }

    [Fact]
    public async Task Answers_404_when_changing_the_status_of_a_project_that_does_not_exist()
    {
        var (controller, _) = ControllerFor(ArchitectId, PlatformRoles.Architect);

        var result = await controller.UpdateProjectStatus(Guid.NewGuid(), Request("DesignApproved"));

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Answers_409_when_somebody_else_moved_the_project_first()
    {
        // The conditional update found a different status than the one that was
        // read and validated against, so nothing was written and the caller is
        // told rather than left believing their change landed.
        var (controller, repository) = ControllerFor(ArchitectId, PlatformRoles.Architect);
        repository.UpdateSucceeds = false;

        var result = await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(ProjectStatus.Designing, repository.Single.Status);
    }

    [Fact]
    public async Task Refuses_a_token_that_carries_no_usable_subject_when_changing_status()
    {
        var (controller, repository) = ControllerFor(ArchitectId, PlatformRoles.Architect, subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved")));

        Assert.Null(repository.UpdatedWith);
    }

    // ------------------------------------------------------------- setup ----

    private static async Task<ProjectDetailResponse> Detail(Task<IActionResult> action) =>
        Assert.IsType<ProjectDetailResponse>(Assert.IsType<OkObjectResult>(await action).Value);

    private static int? StatusOf(IActionResult result) => Assert.IsType<ObjectResult>(result).StatusCode;

    private static UpdateProjectStatusRequest Request(string status) => new() { Status = status };

    /// <summary>
    /// A controller over one project, mid-lifecycle at <c>Designing</c> with an
    /// Architect and a Project Manager on it, and two history entries behind it.
    /// </summary>
    private static (ProjectsController Controller, FakeProjectRepository Repository) ControllerFor(
        Guid userId,
        string role,
        string? subject = null)
    {
        var repository = new FakeProjectRepository();
        repository.Seed(SeedProject(), SeedHistory());

        var controller = new ProjectsController(
            repository,
            new NoOpEventPublisher(),
            NullLogger<ProjectsController>.Instance)
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
                        new Claim(JwtRegisteredClaimNames.Sub, subject ?? userId.ToString()),
                        new Claim(JwtOptions.RoleClaimType, role)
                    ], "TestAuth"))
                }
            }
        };

        return (controller, repository);
    }

    private static Project SeedProject() => new()
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
        OtherRequirements = "Solar hot water",
        Status = ProjectStatus.Designing,
        AssignedArchitectId = ArchitectId,
        AssignedProjectManagerId = ProjectManagerId,
        CreatedAt = CreatedAt,
        UpdatedAt = CreatedAt.AddDays(3)
    };

    private static List<ProjectStatusChange> SeedHistory() =>
    [
        new()
        {
            Id = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
            ProjectId = ProjectId,
            FromStatus = null,
            ToStatus = ProjectStatus.Pending,
            ChangedByUserId = ClientId,
            ChangedByRole = PlatformRoles.Client,
            ChangedAt = CreatedAt
        },
        new()
        {
            Id = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"),
            ProjectId = ProjectId,
            FromStatus = ProjectStatus.Pending,
            ToStatus = ProjectStatus.Designing,
            ChangedByUserId = ProjectManagerId,
            ChangedByRole = PlatformRoles.ProjectManager,
            ChangedAt = CreatedAt.AddDays(3)
        }
    ];

    /// <summary>
    /// Holds projects and history in memory instead of in MySQL, and records
    /// which listing the action asked for and what it wrote.
    /// </summary>
    private sealed class FakeProjectRepository : IProjectRepository
    {
        private readonly List<Project> _projects = [];
        private readonly List<ProjectStatusChange> _history = [];

        /// <summary>The one seeded project, for asserting against what was stored.</summary>
        public Project Single => _projects[0];

        public Guid? ListedForUser { get; private set; }

        public bool ListedAll { get; private set; }

        public ProjectStatusChange? UpdatedWith { get; private set; }

        /// <summary>
        /// Set false to stand in for the conditional UPDATE matching no row —
        /// somebody moved the project between the read and the write.
        /// </summary>
        public bool UpdateSucceeds { get; set; } = true;

        public void Seed(Project project, IEnumerable<ProjectStatusChange> history)
        {
            _projects.Add(project);
            _history.AddRange(history);
        }

        public Task InsertAsync(Project project, ProjectStatusChange creation)
        {
            _projects.Add(project);
            _history.Add(creation);

            return Task.CompletedTask;
        }

        public Task<Project?> GetByIdAsync(Guid id) =>
            Task.FromResult(_projects.SingleOrDefault(project => project.Id == id));

        public Task<IReadOnlyList<Project>> ListAllAsync()
        {
            ListedAll = true;

            return Task.FromResult<IReadOnlyList<Project>>(_projects);
        }

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId)
        {
            ListedForUser = userId;

            return Task.FromResult<IReadOnlyList<Project>>(
            [
                .. _projects.Where(project =>
                    project.ClientId == userId
                    || project.AssignedArchitectId == userId
                    || project.AssignedProjectManagerId == userId)
            ]);
        }

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            Task.FromResult<IReadOnlyList<ProjectStatusChange>>(
            [
                .. _history
                    .Where(change => change.ProjectId == projectId)
                    .OrderBy(change => change.ChangedAt)
                    .ThenBy(change => change.Id)
            ]);

        public Task<bool> UpdateStatusAsync(ProjectStatusChange change, DateTime updatedAtUtc)
        {
            if (!UpdateSucceeds)
            {
                return Task.FromResult(false);
            }

            UpdatedWith = change;
            _history.Add(change);

            var project = _projects.Single(candidate => candidate.Id == change.ProjectId);
            project.Status = change.ToStatus;
            project.UpdatedAt = updatedAtUtc;

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// US-06 publishes nothing — no story has named an event for a status
    /// change — so this exists only to satisfy the constructor and fails loudly
    /// if that ever stops being true by accident.
    /// </summary>
    private sealed class NoOpEventPublisher : IProjectEventPublisher
    {
        public Task PublishProjectCreatedAsync(Project project, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("US-06 publishes no events.");
    }
}
