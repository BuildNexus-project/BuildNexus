using System.Security.Claims;
using BuildNexus.ProjectService.Authorization;
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
        await _projectRepository.InsertAsync(project, ProjectStatusChange.ForCreation(project, PlatformRoles.Client));

        _logger.LogInformation(
            "Created project {ProjectId} for client {ClientId} with status {Status}.",
            project.Id,
            project.ClientId,
            project.Status);

        await PublishProjectCreatedAsync(project);

        return StatusCode(StatusCodes.Status201Created, ToProjectResponse(project));
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
