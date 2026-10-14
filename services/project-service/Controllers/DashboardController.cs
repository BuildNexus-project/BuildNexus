using System.Security.Claims;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Controllers;

/// <summary>
/// The project half of each role's dashboard (US-21). One endpoint per role,
/// each answering only from this service's own database.
/// </summary>
/// <remarks>
/// A dashboard needs data from all five services, and each service's database is
/// its own. So there is no single "dashboard" call: every service exposes the
/// slice it owns, under its own gateway prefix, and the page joins them. What
/// belongs here is what only the Project Service knows — which projects a
/// Client submitted, which an Architect is on, and how many there are in each
/// status.
/// <para>
/// Its own controller rather than more actions on <see cref="ProjectsController"/>:
/// these are aggregate reads shaped for a screen, not the per-project access
/// rule that controller enforces. And one action per role rather than one action
/// that switches on the caller's role, so each shape is declared, gated and
/// documented on its own.
/// </para>
/// <para>
/// Who is asking decides what comes back, and it is always the token's own
/// <c>sub</c> — none of these takes an id. A Client cannot ask for another
/// Client's dashboard because there is nowhere to say whose.
/// </para>
/// </remarks>
[ApiController]
[Route("api/projects/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IProjectDashboardRepository _dashboardRepository;

    public DashboardController(IProjectDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    /// <summary>
    /// The Client's active projects (US-21 AC-1). Allowed roles: Client.
    /// </summary>
    /// <remarks>
    /// Active means neither <c>Completed</c> nor <c>Cancelled</c>. Most recently
    /// moved first. A Client with nothing under way gets an empty list, which is
    /// the truthful answer rather than a refusal.
    /// </remarks>
    /// <response code="200">The Client's active projects.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client.</response>
    [HttpGet("client")]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ClientProjectsDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetClientDashboard(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId))
        {
            return Unauthorized();
        }

        var projects = await _dashboardRepository.ListActiveForClientAsync(clientId, cancellationToken);

        return Ok(new ClientProjectsDashboardResponse
        {
            ActiveCount = projects.Count,
            Projects = projects.Select(DashboardProjectResponse.From).ToList()
        });
    }

    /// <summary>
    /// The Architect's assigned projects (US-21 AC-2). Allowed roles: Architect.
    /// </summary>
    /// <remarks>
    /// Only the active ones — a finished or cancelled project is not work the
    /// Architect still has to do. Most recently moved first. An Architect
    /// nobody has assigned yet gets an empty list.
    /// </remarks>
    /// <response code="200">The Architect's active assigned projects.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Architect.</response>
    [HttpGet("architect")]
    [Authorize(Roles = PlatformRoles.Architect)]
    [ProducesResponseType(typeof(ArchitectProjectsDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetArchitectDashboard(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var architectId))
        {
            return Unauthorized();
        }

        var projects = await _dashboardRepository.ListActiveForArchitectAsync(architectId, cancellationToken);

        return Ok(new ArchitectProjectsDashboardResponse
        {
            AssignedCount = projects.Count,
            Projects = projects.Select(DashboardProjectResponse.From).ToList()
        });
    }

    /// <summary>
    /// The system-wide project count (US-21 AC-4). Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Every project, whatever its status, and how they divide across the
    /// lifecycle — every status is reported, including those nothing is in.
    /// </remarks>
    /// <response code="200">The total and the per-status counts.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet("admin")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(AdminProjectsDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminDashboard(CancellationToken cancellationToken)
    {
        var counts = await _dashboardRepository.CountByStatusAsync(cancellationToken);

        return Ok(AdminProjectsDashboardResponse.From(counts));
    }

    /// <summary>
    /// The caller's account id from the token's <c>sub</c> claim. Signature-valid
    /// but without a usable id is a token there is nobody to answer for.
    /// </summary>
    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
