using System.Security.Claims;
using BuildNexus.ConstructionService.Authorization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Controllers;

/// <summary>
/// The construction half of each role's dashboard (US-21). One endpoint per role,
/// each answering only from this service's own database.
/// </summary>
/// <remarks>
/// A dashboard needs data from all five services, and each service's database is
/// its own. So there is no single "dashboard" call: every service exposes the
/// slice it owns, under its own gateway prefix, and the page joins them. What
/// belongs here is what only the Construction Service knows — how far each build
/// has got and what is left to finish.
/// <para>
/// Its own controller rather than more actions on
/// <see cref="ConstructionProgressController"/> or <see cref="MilestonesController"/>:
/// those are per-project reads and writes with per-project rules, and these are
/// aggregate reads shaped for a screen. Nothing here writes.
/// </para>
/// <para>
/// The class carries a bare <c>[Authorize]</c> and each action names its own role —
/// two role lists on one class and one action would both have to be satisfied, and
/// no caller is both a Client and a Project Manager.
/// </para>
/// </remarks>
[ApiController]
[Route("api/construction/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    /// <summary>
    /// How many outstanding milestones the Project Manager's list shows. The count
    /// beside it is the whole number, so a longer list is a click away, not a
    /// larger response on every dashboard load.
    /// </summary>
    public const int MilestonesDueListSize = 10;

    private readonly IConstructionDashboardRepository _dashboardRepository;

    public DashboardController(IConstructionDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    /// <summary>
    /// How far along the build of each of the Client's projects is (US-21 AC-1).
    /// Allowed roles: Client.
    /// </summary>
    /// <remarks>
    /// Scoped to the caller by this service's own record of who owns each project,
    /// and the endpoint takes no id — a Client cannot ask for another Client's
    /// dashboard because there is nowhere to say whose. A project whose build has
    /// been handed over drops off; one that is planned but not started is listed
    /// with a null phase and zero progress.
    /// </remarks>
    /// <response code="200">The Client's projects with milestones planned, and their progress. An empty list when there are none — a real answer, not an error.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client.</response>
    [HttpGet("client")]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ClientConstructionDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetClientDashboard(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId))
        {
            // The role claim got the caller this far, but without a usable subject
            // there is no "their own projects" to scope the read to.
            return Unauthorized();
        }

        var rows = await _dashboardRepository.ListProgressForClientAsync(clientId, cancellationToken);

        return Ok(new ClientConstructionDashboardResponse
        {
            Projects = rows.Select(ClientConstructionDashboardResponse.ProjectBuildProgressResponse.From).ToList()
        });
    }

    /// <summary>
    /// The builds under way and the milestones still to finish on them (US-21
    /// AC-3). Allowed roles: ProjectManager.
    /// </summary>
    /// <remarks>
    /// Portfolio-wide: this service holds who owns a project but not which Project
    /// Manager runs it, so it cannot narrow to "your" builds — the same scope the
    /// construction report has. "Under way" is a build that has been started and not
    /// yet handed over. "Due" means outstanding, since milestones carry no due date:
    /// the count is every milestone still to finish on those builds, and the list is
    /// the first few — those already in progress, then those next in line.
    /// </remarks>
    /// <response code="200">The active builds, and the outstanding milestones. Both empty when nothing has been started — a real answer, not an error.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    [HttpGet("project-manager")]
    [Authorize(Roles = PlatformRoles.ProjectManager)]
    [ProducesResponseType(typeof(ProjectManagerConstructionDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProjectManagerDashboard(CancellationToken cancellationToken)
    {
        var builds = await _dashboardRepository.ListActiveBuildsAsync(cancellationToken);
        var due = await _dashboardRepository.GetOutstandingMilestonesAsync(MilestonesDueListSize, cancellationToken);

        return Ok(new ProjectManagerConstructionDashboardResponse
        {
            ActiveBuildCount = builds.Count,
            ActiveBuilds = builds.Select(ProjectManagerConstructionDashboardResponse.ActiveBuildResponse.From).ToList(),
            MilestonesDue = new ProjectManagerConstructionDashboardResponse.MilestonesDueResponse
            {
                TotalCount = due.TotalCount,
                Milestones = due.Items.Select(ProjectManagerConstructionDashboardResponse.MilestoneDueResponse.From).ToList()
            }
        });
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
