using BuildNexus.DesignService.Authorization;
using BuildNexus.DesignService.Contracts;
using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Models;
using BuildNexus.DesignService.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.DesignService.Controllers;

/// <summary>
/// The design half of each role's dashboard (US-21). One endpoint per role, each
/// answering only from this service's own database.
/// </summary>
/// <remarks>
/// A dashboard needs data from all five services, and each service's database is
/// its own. So there is no single "dashboard" call: every service exposes the
/// slice it owns, under its own gateway prefix, and the page joins them. What
/// belongs here is what only the Design Service knows — where each project's
/// documents stand in review.
/// <para>
/// This service does not know whose a project is. So each action first asks the
/// Project Service which projects the caller may see, forwarding the caller's own
/// token — the same arrangement the per-project access check uses — and looks
/// only at those. Nobody can widen their dashboard by asking for it differently:
/// none of these takes a project id.
/// </para>
/// <para>
/// Its own controller rather than more actions on <see cref="DesignsController"/>,
/// the way the reports are: these are aggregate reads across projects, not the
/// per-project access rule that controller enforces.
/// </para>
/// </remarks>
[ApiController]
[Route("api/designs/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDesignDashboardRepository _dashboardRepository;
    private readonly IProjectDirectoryClient _projectDirectory;

    public DashboardController(
        IDesignDashboardRepository dashboardRepository,
        IProjectDirectoryClient projectDirectory)
    {
        _dashboardRepository = dashboardRepository;
        _projectDirectory = projectDirectory;
    }

    /// <summary>
    /// Where the design stands on each of the Client's active projects (US-21
    /// AC-1). Allowed roles: Client.
    /// </summary>
    /// <remarks>
    /// Every active project is reported, in the Project Service's order (most
    /// recently moved first), including one nothing has been uploaded for —
    /// <c>NoDesign</c> is a status, not an absence. Each project's state is read
    /// from its documents' latest versions: <c>AwaitingReview</c> when something
    /// is waiting on the Client, <c>RevisionRequested</c> when the Client is
    /// waiting on the Architect, <c>Approved</c> when every document is signed off.
    /// </remarks>
    /// <response code="200">The design status of each active project.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client.</response>
    /// <response code="502">The Project Service could not be reached to list the caller's projects.</response>
    [HttpGet("client")]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ClientDesignDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetClientDashboard(CancellationToken cancellationToken)
    {
        if (CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        var visible = await _projectDirectory.ListVisibleProjectsAsync(token, cancellationToken);

        if (!visible.IsAvailable)
        {
            return ProjectsUnavailable();
        }

        var projectIds = visible.ActiveProjectIds();
        var tallies = (await _dashboardRepository.GetDesignTalliesAsync(projectIds, cancellationToken))
            .ToDictionary(tally => tally.ProjectId);

        return Ok(new ClientDesignDashboardResponse
        {
            Projects = projectIds
                .Select(id => tallies.GetValueOrDefault(id) ?? ProjectDesignTally.Empty(id))
                .Select(ClientDesignDashboardResponse.ProjectDesignStatusResponse.From)
                .ToList()
        });
    }

    /// <summary>
    /// The revisions Clients have asked for that the Architect has not answered
    /// (US-21 AC-2). Allowed roles: Architect.
    /// </summary>
    /// <remarks>
    /// Limited to the projects the Architect is assigned to, which the Project
    /// Service decides. A revision counts as pending while it is the latest
    /// version of its document — uploading a newer version answers it. Longest
    /// waiting first.
    /// </remarks>
    /// <response code="200">The Architect's pending revisions.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Architect.</response>
    /// <response code="502">The Project Service could not be reached to list the caller's projects.</response>
    [HttpGet("architect")]
    [Authorize(Roles = PlatformRoles.Architect)]
    [ProducesResponseType(typeof(ArchitectDesignDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetArchitectDashboard(CancellationToken cancellationToken)
    {
        if (CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        var visible = await _projectDirectory.ListVisibleProjectsAsync(token, cancellationToken);

        if (!visible.IsAvailable)
        {
            return ProjectsUnavailable();
        }

        var revisions = await _dashboardRepository.ListPendingRevisionsAsync(
            visible.ActiveProjectIds(), cancellationToken);

        return Ok(new ArchitectDesignDashboardResponse
        {
            PendingRevisionCount = revisions.Count,
            Revisions = revisions.Select(ArchitectDesignDashboardResponse.PendingRevisionResponse.From).ToList()
        });
    }

    /// <summary>
    /// A Project Service that could not answer is not a Client with no projects —
    /// saying "nothing to show" would be a wrong answer that looks right.
    /// </summary>
    private IActionResult ProjectsUnavailable() =>
        Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Projects could not be listed",
            detail: "Your projects could not be looked up right now, so the design summary cannot be shown. "
                    + "Try again in a moment.");

    /// <summary>
    /// The raw access token from the <c>Authorization</c> header, without the
    /// <c>Bearer </c> prefix — forwarded to the Project Service so it decides as
    /// if the caller asked it directly.
    /// </summary>
    private string? CallerBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim() is { Length: > 0 } value ? value : null
            : null;
    }
}
