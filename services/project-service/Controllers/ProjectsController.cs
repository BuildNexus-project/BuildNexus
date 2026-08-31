using System.Security.Claims;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
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
    private readonly IProjectEventPublisher _eventPublisher;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectRepository projectRepository,
        IProjectEventPublisher eventPublisher,
        ILogger<ProjectsController> logger)
    {
        _projectRepository = projectRepository;
        _eventPublisher = eventPublisher;
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

        // Stored with the opening entry of its status history, in one
        // transaction. The creation is the first thing the audit trail has to
        // say about the project, and a history that starts later is not the
        // full record US-06 asks the view to show.
        // No events yet: US-22 moves ProjectCreated onto the outbox once the
        // payloads and the dispatcher behind them exist. Until then the publish
        // below is still the direct one US-05 wrote.
        await _projectRepository.InsertAsync(
            project,
            ProjectStatusChange.ForCreation(project, PlatformRoles.Client),
            []);

        _logger.LogInformation(
            "Created project {ProjectId} for client {ClientId} with status {Status}.",
            project.Id,
            project.ClientId,
            project.Status);

        await PublishProjectCreatedAsync(project);

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

        if (!await _projectRepository.UpdateStatusAsync(change, now, []))
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
            "Project {ProjectId} moved from {FromStatus} to {ToStatus} by {UserId} in role {Role}.",
            project.Id,
            change.FromStatus,
            change.ToStatus,
            userId,
            change.ChangedByRole);

        // Answer with the project as it now stands rather than as it was read,
        // so the screen that made the change does not have to fetch it again.
        project.Status = target;
        project.UpdatedAt = now;

        var history = await _projectRepository.GetStatusHistoryAsync(project.Id);

        return Ok(ProjectDetailResponse.From(project, history));
    }

    /// <summary>
    /// Announces the new project on <c>project-events</c>, so the Design,
    /// Construction and Payment services learn about it without polling this
    /// one.
    /// </summary>
    /// <remarks>
    /// A failed publish does not fail the request. The row is already committed
    /// by the time this runs, so answering with a 500 would tell the Client
    /// their submission was lost when it was not — and a retry would create a
    /// second project. It is logged at error level with the project id instead,
    /// which is enough to republish it by hand.
    /// <para>
    /// That leaves a real gap: a project created while the broker is unreachable
    /// is never announced. Closing it properly means writing the event into this
    /// service's own database in the same transaction as the row and having a
    /// background worker drain it — the transactional outbox pattern — which is
    /// its own story rather than something to smuggle in here.
    /// </para>
    /// </remarks>
    private async Task PublishProjectCreatedAsync(Project project)
    {
        try
        {
            // Deliberately not given the request's cancellation token: the row
            // is already committed, and a Client closing the tab must not leave
            // a project nobody was told about. The publish is bounded by
            // Kafka:MessageTimeoutMs instead.
            await _eventPublisher.PublishProjectCreatedAsync(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Project {ProjectId} was created but its {EventType} event could not be published.",
                project.Id,
                ProjectEventTypes.ProjectCreated);
        }
    }

    /// <summary>
    /// The role the caller's token carries, as one of the four platform role
    /// names. Recorded on a status change exactly as it was at the time.
    /// </summary>
    private string? CallerRole() => User.FindFirstValue(JwtOptions.RoleClaimType);

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
