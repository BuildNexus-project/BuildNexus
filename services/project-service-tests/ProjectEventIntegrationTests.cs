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
/// The US-22 acceptance criteria at the endpoint: what a state change announces,
/// and who may see whether it got out.
/// </summary>
/// <remarks>
/// Over the same in-memory stand-ins the other controller suites use, so these
/// need neither MySQL nor a broker. What is under test is the action's own
/// decisions — which events it raises, that it hands them to the write rather
/// than publishing them itself, and who it refuses.
/// </remarks>
public class ProjectEventIntegrationTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid ArchitectId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------- what a status change raises ----

    [Fact]
    public async Task A_status_change_raises_ProjectUpdated()
    {
        // The first AC bullet at the endpoint: a project that moved is a project
        // somebody was told about.
        var (controller, repository, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.Contains(repository.RaisedEvents, e => e.EventType == ProjectEventTypes.ProjectUpdated);
    }

    [Fact]
    public async Task Approving_a_design_raises_the_approval_too()
    {
        var (controller, repository, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.Equal(
            [ProjectEventTypes.ProjectUpdated, ProjectEventTypes.ProjectApproved],
            repository.RaisedEvents.Select(e => e.EventType));
    }

    [Fact]
    public async Task The_events_are_handed_to_the_write_rather_than_published_separately()
    {
        // The whole of "publishes reliably": the events go into the same
        // transaction as the move, so there is no window in which the project
        // moved and nothing was ever going to announce it.
        var (controller, repository, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.NotNull(repository.UpdatedWith);
        Assert.NotEmpty(repository.RaisedEvents);
    }

    [Fact]
    public async Task A_move_that_lost_a_race_announces_nothing()
    {
        // The guarded UPDATE wrote nothing, so the events it carried were rolled
        // back with it. Announcing a move that did not happen would leave the
        // other services acting on a state this one is not in.
        var (controller, repository, _) = ControllerFor(AdminId, PlatformRoles.Admin);
        repository.UpdateSucceeds = false;

        var result = await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(repository.RaisedEvents);
    }

    [Fact]
    public async Task A_refused_move_announces_nothing()
    {
        // The lifecycle refused it before anything was written.
        var (controller, repository, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        var result = await controller.UpdateProjectStatus(ProjectId, Request("Completed"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(repository.RaisedEvents);
    }

    [Fact]
    public async Task The_raised_events_describe_the_move_that_was_actually_made()
    {
        // Built from the change, not from the project — which still holds the
        // status it is being moved off at that point.
        var (controller, repository, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        await controller.UpdateProjectStatus(ProjectId, Request("DesignApproved"));

        var update = repository.RaisedEvents.Single(e => e.EventType == ProjectEventTypes.ProjectUpdated);
        Assert.Equal(ProjectId, update.ProjectId);
        Assert.Contains("\"previousStatus\":\"Designing\"", update.Envelope);
        Assert.Contains("\"status\":\"DesignApproved\"", update.Envelope);
        Assert.Contains($"\"changedByUserId\":\"{AdminId}\"", update.Envelope);
    }

    // ------------------------------------------------------ the events view ----

    [Fact]
    public async Task An_admin_sees_a_projects_events_oldest_first()
    {
        var (controller, _, outbox) = ControllerFor(AdminId, PlatformRoles.Admin);
        outbox.Seed(Recorded(2, ProjectEventTypes.ProjectUpdated), Recorded(1, ProjectEventTypes.ProjectCreated));

        var events = await Events(controller.GetProjectEvents(ProjectId));

        Assert.Equal(
            [ProjectEventTypes.ProjectCreated, ProjectEventTypes.ProjectUpdated],
            events.Select(e => e.EventType));
    }

    [Fact]
    public async Task The_view_says_whether_each_event_got_out()
    {
        // The reason this endpoint exists: the publish no longer happens inside
        // the request, so nothing else on the API can answer it.
        var (controller, _, outbox) = ControllerFor(AdminId, PlatformRoles.Admin);
        var delivered = Recorded(1, ProjectEventTypes.ProjectCreated);
        delivered.PublishedAt = CreatedAt.AddSeconds(2);
        delivered.AttemptCount = 3;

        var stuck = Recorded(2, ProjectEventTypes.ProjectUpdated);
        stuck.AttemptCount = 7;
        stuck.LastError = "Local: Message timed out";

        outbox.Seed(delivered, stuck);

        var events = await Events(controller.GetProjectEvents(ProjectId));

        Assert.Equal(CreatedAt.AddSeconds(2), events[0].PublishedAt);
        Assert.Equal(3, events[0].AttemptCount);
        Assert.Null(events[0].LastError);

        Assert.Null(events[1].PublishedAt);
        Assert.Equal(7, events[1].AttemptCount);
        Assert.Equal("Local: Message timed out", events[1].LastError);
    }

    [Fact]
    public async Task A_project_that_has_raised_nothing_yet_comes_back_empty()
    {
        // A real answer, not a refusal: a project created before the outbox
        // existed legitimately has no events.
        var (controller, _, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        Assert.Empty(await Events(controller.GetProjectEvents(ProjectId)));
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_a_404_rather_than_an_empty_list()
    {
        // The two mean different things, and answering 200 with [] for both
        // would hide a typo behind a plausible answer.
        var (controller, _, _) = ControllerFor(AdminId, PlatformRoles.Admin);

        var result = await controller.GetProjectEvents(Guid.Parse("00000000-0000-4000-8000-000000000000"));

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task A_token_with_no_usable_subject_is_refused()
    {
        var (controller, _, _) = ControllerFor(AdminId, PlatformRoles.Admin, subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetProjectEvents(ProjectId));
    }

    [Fact]
    public void The_events_view_is_admin_only()
    {
        // Declared on the action rather than checked in it, so this reads the
        // attribute the framework actually enforces. Delivery state and broker
        // error text are operations data — the staff on a project cannot act on
        // a failed publish, and the owning Client has no use for it at all.
        var roles = typeof(ProjectsController)
            .GetMethod(nameof(ProjectsController.GetProjectEvents))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        Assert.Equal([PlatformRoles.Admin], roles);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<IReadOnlyList<ProjectEventResponse>> Events(Task<IActionResult> action) =>
        Assert.IsType<List<ProjectEventResponse>>(Assert.IsType<OkObjectResult>(await action).Value);

    private static int? StatusOf(IActionResult result) => Assert.IsType<ObjectResult>(result).StatusCode;

    private static UpdateProjectStatusRequest Request(string status) => new() { Status = status };

    private static OutboxEvent Recorded(long sequenceNumber, string eventType) => new()
    {
        SequenceNumber = sequenceNumber,
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        EventType = eventType,
        Envelope = "{}",
        OccurredAt = CreatedAt
    };

    /// <summary>
    /// A controller over one project sitting at <c>Designing</c>, so the next
    /// move is the approval — the transition that raises both events.
    /// </summary>
    private static (ProjectsController Controller, StubRepository Repository, FakeOutboxRepository Outbox)
        ControllerFor(Guid userId, string role, string? subject = null)
    {
        var repository = new StubRepository();
        var outbox = new FakeOutboxRepository();

        var controller = new ProjectsController(repository, outbox, NullLogger<ProjectsController>.Instance)
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

        return (controller, repository, outbox);
    }

    /// <summary>
    /// Holds one project and records what a status change asked to write.
    /// </summary>
    private sealed class StubRepository : IProjectRepository
    {
        private readonly Project _project = new()
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
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        };

        public bool UpdateSucceeds { get; set; } = true;

        public ProjectStatusChange? UpdatedWith { get; private set; }

        /// <summary>The events the action asked to enqueue with the move.</summary>
        public IReadOnlyList<OutboxEvent> RaisedEvents { get; private set; } = [];

        public Task InsertAsync(
            Project project,
            ProjectStatusChange creation,
            IReadOnlyList<OutboxEvent> outboxEvents) => Task.CompletedTask;

        public Task<Project?> GetByIdAsync(Guid id) =>
            Task.FromResult(id == ProjectId ? _project : null);

        public Task<IReadOnlyList<Project>> ListAllAsync() =>
            Task.FromResult<IReadOnlyList<Project>>([_project]);

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) =>
            Task.FromResult<IReadOnlyList<Project>>([_project]);

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            Task.FromResult<IReadOnlyList<ProjectStatusChange>>([]);

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents)
        {
            if (!UpdateSucceeds)
            {
                // Nothing is recorded, and that includes the events: the real
                // repository enqueues them inside the transaction the guarded
                // UPDATE just refused.
                return Task.FromResult(false);
            }

            UpdatedWith = change;
            RaisedEvents = outboxEvents;

            return Task.FromResult(true);
        }
    }
}
