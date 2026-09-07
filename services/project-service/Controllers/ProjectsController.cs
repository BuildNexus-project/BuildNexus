using System.Security.Claims;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using BuildNexus.ProjectService.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Controllers;

/// <summary>
/// Construction projects. Every action here requires a valid bearer token — an
/// expired, tampered or malformed token is rejected with 401 before the action
/// runs — and names the roles allowed to call it, so a caller holding a good
/// token for the wrong role is refused with 403.
/// </summary>
/// <remarks>
/// The token is re-validated here whatever the API Gateway did in front of it.
/// The gateway checks the signature and expiry and forwards the token
/// unchanged; it makes no authorization decisions, and this service does not
/// assume anything upstream did.
/// </remarks>
[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUserDirectoryClient _userDirectory;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectRepository projectRepository,
        IOutboxRepository outboxRepository,
        IUserDirectoryClient userDirectory,
        ILogger<ProjectsController> logger)
    {
        _projectRepository = projectRepository;
        _outboxRepository = outboxRepository;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Submits a new construction project. Allowed roles: Client.
    /// </summary>
    /// <remarks>
    /// Client only, and deliberately so: US-05 is a customer describing the
    /// building they want. Staff roles do not submit work on a Client's behalf
    /// — an Architect or Project Manager holding a valid token is refused with
    /// 403, and so is an Admin.
    /// <para>
    /// The project is created with status <c>Pending</c>: it has been submitted
    /// and is waiting on the company to pick it up. That is the only status the
    /// caller can produce, and it is set here rather than taken from the
    /// payload.
    /// </para>
    /// </remarks>
    /// <response code="201">The project was created, and is returned as stored.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller holds a valid token but is not a Client.</response>
    [HttpPost]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        if (!TryGetCallerId(out var clientId))
        {
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            // The owner comes from the caller's own token, never from the
            // payload: a Client can only ever submit a project for themselves.
            ClientId = clientId,
            Name = request.Name.Trim(),
            Location = request.Location.Trim(),
            // The nullable fields are non-null by here — [Required] refused the
            // request before the action was entered if any were missing.
            LandSizePerches = request.LandSizePerches!.Value,
            Budget = request.Budget!.Value,
            Floors = request.Floors!.Value,
            Bedrooms = request.Bedrooms!.Value,
            Bathrooms = request.Bathrooms!.Value,
            GarageSpaces = request.GarageSpaces!.Value,
            OtherRequirements = NullIfBlank(request.OtherRequirements),
            // Set here, not taken from the payload: a submitted project is
            // Pending, and the caller has no say in that.
            Status = ProjectStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Stored with the opening entry of its status history and its
        // ProjectCreated event, in one transaction. The creation is the first
        // thing the audit trail has to say about the project, and a history
        // that starts later is not the full record US-06 asks the view to show.
        //
        // The event rides along rather than being published after the commit
        // (US-22): a broker that was unreachable used to mean a project nobody
        // was ever told about, and no amount of logging turned that back into a
        // delivery. On the outbox it is sent late instead of not at all, and
        // nothing here waits for Kafka to answer.
        await _projectRepository.InsertAsync(
            project,
            ProjectStatusChange.ForCreation(project, PlatformRoles.Client),
            [ProjectEvents.Created(project)]);

        _logger.LogInformation(
            "Created project {ProjectId} for client {ClientId} with status {Status}.",
            project.Id,
            project.ClientId,
            project.Status);

        return StatusCode(StatusCodes.Status201Created, ToProjectResponse(project));
    }

    /// <summary>
    /// Lists the projects the caller may see. Allowed roles: Client, Architect,
    /// Project Manager, Admin.
    /// </summary>
    /// <remarks>
    /// The way into the project view, and scoped by the same rule that guards
    /// it: a Client sees the projects they submitted, an Architect or Project
    /// Manager the ones they are assigned to, and an Admin all of them. Nobody
    /// is shown a project they would be refused when they clicked it.
    /// <para>
    /// Every role is admitted to the endpoint because the role is not what
    /// decides the answer here — the caller's own id is. A caller with nothing
    /// to their name gets an empty list, which is the truthful answer rather
    /// than a refusal.
    /// </para>
    /// </remarks>
    /// <response code="200">The projects the caller may see, newest first.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    [HttpGet]
    [Authorize(Roles = PlatformRoles.AnyRole)]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListProjects()
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        var projects = ProjectAccessPolicy.SeesEveryProject(CallerRole())
            ? await _projectRepository.ListAllAsync()
            : await _projectRepository.ListForUserAsync(userId);

        return Ok(projects.Select(ProjectSummaryResponse.From).ToList());
    }

    /// <summary>
    /// One project in full, with its status history. Allowed roles: Client,
    /// Architect, Project Manager, Admin.
    /// </summary>
    /// <remarks>
    /// The role gets a caller as far as the endpoint; whether they may see
    /// <em>this</em> project is <see cref="ProjectAccessPolicy.CanView"/>'s
    /// answer, and it can only be asked once the row has been read. Being an
    /// Architect is not enough — it has to be this project's Architect.
    /// <para>
    /// A caller who is not party to the project is told so with a 403 rather
    /// than a 404. That does reveal the id exists, which is a deliberate trade:
    /// these are four known internal roles, not the open internet, and "you are
    /// not on this project" is something somebody can act on where a 404 sends
    /// them looking for a typo that is not there.
    /// </para>
    /// </remarks>
    /// <response code="200">The project, its requirements, and its full status history.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not the owning client, assigned staff, or an Admin.</response>
    /// <response code="404">No project has that id.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = PlatformRoles.AnyRole)]
    [ProducesResponseType(typeof(ProjectDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProject(Guid id)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        var project = await _projectRepository.GetByIdAsync(id);

        if (project is null)
        {
            return ProjectNotFound(id);
        }

        if (!ProjectAccessPolicy.CanView(project, userId, CallerRole()))
        {
            return NotOnThisProject(id, userId, "view");
        }

        var history = await _projectRepository.GetStatusHistoryAsync(project.Id);

        return Ok(ProjectDetailResponse.From(project, history));
    }

    /// <summary>
    /// Moves a project to its next status and records the change. Allowed
    /// roles: Architect, Project Manager, Admin.
    /// </summary>
    /// <remarks>
    /// The owning Client is deliberately outside this. They can see every step
    /// of their project, but declaring the design approved or the build
    /// finished is the company's word, not the customer's.
    /// <para>
    /// The move itself must be one <see cref="ProjectStatusTransitions"/>
    /// allows — forward, one stage at a time — so a build cannot start before
    /// its design is approved and a finished project cannot be reopened. Who
    /// made the change is taken from the caller's own token and never from the
    /// payload; a history that could be attributed to somebody else would not
    /// be worth keeping.
    /// </para>
    /// </remarks>
    /// <response code="200">The project as it now stands, with the new entry in its history.</response>
    /// <response code="400">The status is not a status, or not one this project may move to.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not assigned staff on this project, nor an Admin.</response>
    /// <response code="404">No project has that id.</response>
    /// <response code="409">Somebody else moved the project first.</response>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = PlatformRoles.ProjectStaffOrAdmin)]
    [ProducesResponseType(typeof(ProjectDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProjectStatus(Guid id, [FromBody] UpdateProjectStatusRequest request)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        var callerRole = CallerRole();

        var project = await _projectRepository.GetByIdAsync(id);

        if (project is null)
        {
            return ProjectNotFound(id);
        }

        if (!ProjectAccessPolicy.CanUpdateStatus(project, userId, callerRole))
        {
            return NotOnThisProject(id, userId, "change the status of");
        }

        // [EnumDataType] already refused anything that is not a status name, so
        // this parses.
        var target = Enum.Parse<ProjectStatus>(request.Status);

        if (!ProjectStatusTransitions.IsAllowed(project.Status, target))
        {
            return InvalidTransition(project, target);
        }

        var now = DateTime.UtcNow;
        var change = new ProjectStatusChange
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            FromStatus = project.Status,
            ToStatus = target,
            // From the token, never the payload: an audit trail that could be
            // attributed to somebody else is not worth keeping.
            ChangedByUserId = userId,
            ChangedByRole = callerRole ?? string.Empty,
            ChangedAt = now
        };

        // The events this move announces, enqueued in the same transaction as the
        // move itself (US-22). Built before the write because that is what the
        // write is given; their contents come from the change rather than from
        // the project, which still holds the old status at this point.
        var raised = ProjectEvents.ForStatusChange(project, change);

        if (!await _projectRepository.UpdateStatusAsync(change, now, raised))
        {
            // The guard in the UPDATE found a status other than the one we read
            // and validated against, so somebody moved the project in between
            // and nothing was written.
            _logger.LogWarning(
                "Project {ProjectId} moved before {UserId} could change it from {FromStatus} to {ToStatus}.",
                project.Id,
                userId,
                change.FromStatus,
                change.ToStatus);

            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The project has moved on",
                detail: "Somebody else changed this project's status while you were looking at it. "
                        + "Reload it to see where it stands now.");
        }

        _logger.LogInformation(
            "Project {ProjectId} moved from {FromStatus} to {ToStatus} by {UserId} in role {Role}, "
            + "raising {EventTypes}.",
            project.Id,
            change.FromStatus,
            change.ToStatus,
            userId,
            change.ChangedByRole,
            string.Join(" and ", raised.Select(e => e.EventType)));

        // Answer with the project as it now stands rather than as it was read,
        // so the screen that made the change does not have to fetch it again.
        project.Status = target;
        project.UpdatedAt = now;

        var history = await _projectRepository.GetStatusHistoryAsync(project.Id);

        return Ok(ProjectDetailResponse.From(project, history));
    }

    /// <summary>
    /// Puts an Architect on a project. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// US-07: only an Admin assigns staff, and the account named must actually
    /// hold the Architect role — checked against the User Service with the
    /// Admin's own forwarded token, since this service holds no copy of who is
    /// what.
    /// <para>
    /// Assigning an Architect to a project that is still <c>Pending</c> also
    /// moves it to <c>Designing</c>: the same transition
    /// <c>PATCH /status</c> would make, with the same history row and the same
    /// <c>ProjectUpdated</c> event, written in one transaction with the
    /// assignment. A project already past <c>Pending</c> just has the slot set
    /// or replaced, and nothing about the lifecycle changes.
    /// </para>
    /// <para>
    /// Whether the account is still active is not checked here — <c>GET
    /// /api/users/{id}</c> reports the role but not the status. The directory
    /// the Admin picks from lists only active staff, and tightening this needs
    /// the User Service to carry <c>isActive</c> on that response.
    /// </para>
    /// </remarks>
    /// <response code="200">The project as it now stands, with the new history entry if it moved.</response>
    /// <response code="400">The payload failed validation, or the account is unknown or not an Architect.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller holds a valid token but is not an Admin.</response>
    /// <response code="404">No project has that id.</response>
    /// <response code="409">Somebody moved the project off Pending first.</response>
    /// <response code="502">The User Service could not be reached to check the account's role.</response>
    [HttpPut("{id:guid}/architect")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(ProjectDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> AssignArchitect(
        Guid id,
        [FromBody] AssignArchitectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var adminId) || CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        var project = await _projectRepository.GetByIdAsync(id);

        if (project is null)
        {
            return ProjectNotFound(id);
        }

        var architectId = request.ArchitectId!.Value;

        if (await CheckAssigneeRoleAsync(
                architectId, PlatformRoles.Architect, "an Architect", nameof(request.ArchitectId), token, cancellationToken)
            is { } refusal)
        {
            return refusal;
        }

        var now = DateTime.UtcNow;

        // Pending → Designing rides along only when the project has not moved
        // yet. Built here because that is what the write is given; ForStatusChange
        // returns just ProjectUpdated for this move, since Designing is not the
        // approval milestone.
        ProjectStatusChange? transition = project.Status == ProjectStatus.Pending
            ? new ProjectStatusChange
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                FromStatus = ProjectStatus.Pending,
                ToStatus = ProjectStatus.Designing,
                ChangedByUserId = adminId,
                ChangedByRole = CallerRole() ?? string.Empty,
                ChangedAt = now
            }
            : null;

        var raised = transition is null
            ? []
            : ProjectEvents.ForStatusChange(project, transition);

        if (!await _projectRepository.AssignArchitectAsync(project.Id, architectId, transition, raised, now))
        {
            return ProjectMovedOn();
        }

        _logger.LogInformation(
            "Admin {AdminId} assigned architect {ArchitectId} to project {ProjectId}{Transition}.",
            adminId,
            architectId,
            project.Id,
            transition is null ? string.Empty : "; project moved Pending -> Designing");

        // Answer with the project as it now stands rather than re-reading it.
        project.AssignedArchitectId = architectId;
        project.UpdatedAt = now;

        if (transition is not null)
        {
            project.Status = ProjectStatus.Designing;
        }

        var history = await _projectRepository.GetStatusHistoryAsync(project.Id);

        return Ok(ProjectDetailResponse.From(project, history));
    }

    /// <summary>
    /// Puts a Project Manager on a project. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// US-07: only an Admin assigns staff, and the account named must hold the
    /// Project Manager role — checked against the User Service the same way
    /// <see cref="AssignArchitect"/> checks the Architect.
    /// <para>
    /// No status change and nothing announced. A PM is typically put on a
    /// project around or after its design is approved, but that is a matter of
    /// when an Admin does this, not a rule this endpoint enforces — the slot can
    /// be filled or changed at any point in the lifecycle.
    /// </para>
    /// </remarks>
    /// <response code="200">The project as it now stands.</response>
    /// <response code="400">The payload failed validation, or the account is unknown or not a Project Manager.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller holds a valid token but is not an Admin.</response>
    /// <response code="404">No project has that id.</response>
    /// <response code="409">The project was removed first.</response>
    /// <response code="502">The User Service could not be reached to check the account's role.</response>
    [HttpPut("{id:guid}/project-manager")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(ProjectDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> AssignProjectManager(
        Guid id,
        [FromBody] AssignProjectManagerRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var adminId) || CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        var project = await _projectRepository.GetByIdAsync(id);

        if (project is null)
        {
            return ProjectNotFound(id);
        }

        var projectManagerId = request.ProjectManagerId!.Value;

        if (await CheckAssigneeRoleAsync(
                projectManagerId, PlatformRoles.ProjectManager, "a Project Manager", nameof(request.ProjectManagerId), token, cancellationToken)
            is { } refusal)
        {
            return refusal;
        }

        var now = DateTime.UtcNow;

        if (!await _projectRepository.AssignProjectManagerAsync(project.Id, projectManagerId, now))
        {
            return ProjectMovedOn();
        }

        _logger.LogInformation(
            "Admin {AdminId} assigned project manager {ProjectManagerId} to project {ProjectId}.",
            adminId,
            projectManagerId,
            project.Id);

        project.AssignedProjectManagerId = projectManagerId;
        project.UpdatedAt = now;

        var history = await _projectRepository.GetStatusHistoryAsync(project.Id);

        return Ok(ProjectDetailResponse.From(project, history));
    }

    /// <summary>
    /// The events this service has raised for one project, and whether each one
    /// reached the message bus. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Admin only, and narrower than every other read here on purpose. This is
    /// not project information — it is the integration answering for itself:
    /// delivery state, attempt counts, and broker error text that names our own
    /// infrastructure. A Client watching their build has no use for it, and the
    /// staff working the project cannot act on a failed publish either; the
    /// person who can is the one administering the platform.
    /// <para>
    /// It exists because a publish no longer happens inside the request that
    /// caused it (US-22). That is what makes the publish reliable, but it also
    /// means "did the other services get told?" stopped being answerable from
    /// the response to the change — and an outbox nobody can see is a queue that
    /// silently stops draining. This is the view that makes the first acceptance
    /// bullet observable rather than merely intended.
    /// </para>
    /// <para>
    /// Oldest first, matching the order they were raised and the order they go
    /// on the topic.
    /// </para>
    /// </remarks>
    /// <response code="200">The project's events, oldest first. Empty for a project raised before the outbox existed.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller holds a valid token but is not an Admin.</response>
    /// <response code="404">No project has that id.</response>
    [HttpGet("{id:guid}/events")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectEvents(Guid id)
    {
        if (!TryGetCallerId(out _))
        {
            return Unauthorized();
        }

        // Read the project first so an id that does not exist is a 404 rather
        // than an empty list — the two mean different things, and an empty list
        // is a real answer for a project created before the outbox existed.
        if (await _projectRepository.GetByIdAsync(id) is null)
        {
            return ProjectNotFound(id);
        }

        // No per-project check beyond that: the role attribute already limited
        // this to Admin, and an Admin is party to every project.
        var events = await _outboxRepository.ListForProjectAsync(id);

        return Ok(events.Select(ProjectEventResponse.From).ToList());
    }

    /// <summary>
    /// The role the caller's token carries, as one of the four platform role
    /// names. Recorded on a status change exactly as it was at the time.
    /// </summary>
    private string? CallerRole() => User.FindFirstValue(JwtOptions.RoleClaimType);

    /// <summary>
    /// The raw access token from the <c>Authorization</c> header, without the
    /// <c>Bearer </c> prefix — forwarded to the User Service so it decides the
    /// <c>GET /api/users/{id}</c> read as if the Admin asked it directly.
    /// </summary>
    private string? CallerBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim() is { Length: > 0 } value ? value : null
            : null;
    }

    /// <summary>
    /// Asks the User Service whether <paramref name="assigneeId"/> holds
    /// <paramref name="expectedRole"/>. Returns <c>null</c> to carry on, or the
    /// response to send back instead.
    /// </summary>
    private async Task<IActionResult?> CheckAssigneeRoleAsync(
        Guid assigneeId,
        string expectedRole,
        string humanRole,
        string field,
        string token,
        CancellationToken cancellationToken)
    {
        var lookup = await _userDirectory.GetUserAsync(assigneeId, token, cancellationToken);

        switch (lookup.Outcome)
        {
            case UserLookupOutcome.Found when lookup.HasRole(expectedRole):
                return null;

            case UserLookupOutcome.Found:
                return AssigneeRejected(field, $"That account is not {humanRole}.");

            case UserLookupOutcome.NotFound:
                return AssigneeRejected(field, "No account with that id.");

            default:
                _logger.LogWarning(
                    "Could not check the role of {AssigneeId} with the User Service; assignment refused.", assigneeId);

                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "Could not verify the account",
                    detail: "The account's role could not be checked with the User Service right now. "
                            + "Try again in a moment.");
        }
    }

    /// <summary>
    /// A bad assignee — unknown, or the wrong role. Returned as a field error so
    /// the offending control in the form is the one that lights up.
    /// </summary>
    private IActionResult AssigneeRejected(string field, string message)
    {
        _logger.LogWarning("Assignment refused: {Field} — {Message}", field, message);

        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [field] = [message]
        }));
    }

    /// <summary>
    /// The project changed under the caller between the read and the write —
    /// moved off Pending, or removed. Same wording as a status-change conflict.
    /// </summary>
    private IActionResult ProjectMovedOn() =>
        Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The project has moved on",
            detail: "Somebody changed this project while you were looking at it. "
                    + "Reload it to see where it stands now.");

    /// <summary>No project with that id — as far as this service is concerned, it does not exist.</summary>
    private IActionResult ProjectNotFound(Guid id) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Project not found",
            detail: $"No project with id '{id}' exists.");

    /// <summary>
    /// The project exists, but the caller is not party to it. Logged with the
    /// caller and the project, so an unexpected refusal can be traced rather
    /// than guessed at.
    /// </summary>
    private IActionResult NotOnThisProject(Guid projectId, Guid userId, string action)
    {
        _logger.LogWarning(
            "Refused {UserId} in role {Role} on project {ProjectId}: not the owning client, assigned staff, or an Admin.",
            userId,
            CallerRole() ?? "none",
            projectId);

        return Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Not your project",
            detail: "Only the client who submitted this project, the staff assigned to it, or an "
                    + $"administrator can {action} it.");
    }

    /// <summary>
    /// A move the lifecycle does not allow. The message names where the project
    /// actually is and what it may do next, because "invalid transition" on its
    /// own tells the caller nothing they can act on.
    /// </summary>
    private IActionResult InvalidTransition(Project project, ProjectStatus target)
    {
        var allowed = ProjectStatusTransitions.NextFrom(project.Status);

        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Not a valid status change",
            detail: allowed.Count == 0
                ? $"This project is {project.Status} and cannot move any further."
                : $"A {project.Status} project cannot move to {target}. It can only move to "
                  + $"{string.Join(" or ", allowed)}.");
    }

    /// <summary>
    /// Reads the caller's id from the token's <c>sub</c> claim. A token that
    /// passed signature validation but carries no usable subject is not
    /// something we can act on.
    /// </summary>
    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    /// <summary>
    /// Blank in, <c>null</c> out — a requirements box the Client left empty must
    /// store nothing rather than an empty string.
    /// </summary>
    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ProjectResponse ToProjectResponse(Project project) => new()
    {
        Id = project.Id,
        ClientId = project.ClientId,
        Name = project.Name,
        Location = project.Location,
        LandSizePerches = project.LandSizePerches,
        Budget = project.Budget,
        Floors = project.Floors,
        Bedrooms = project.Bedrooms,
        Bathrooms = project.Bathrooms,
        GarageSpaces = project.GarageSpaces,
        OtherRequirements = project.OtherRequirements,
        Status = project.Status.ToString(),
        CreatedAt = project.CreatedAt
    };
}
