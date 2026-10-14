using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.UserService.Controllers;

/// <summary>
/// The user half of the Admin dashboard (US-21). Every action requires a valid
/// bearer token and is Admin-only.
/// </summary>
/// <remarks>
/// A dashboard needs data from all five services, and each service's database is
/// its own. So there is no single "dashboard" call: every service exposes the
/// slice it owns, under its own gateway prefix, and the page joins them. What
/// belongs here is what only the User Service knows — how many accounts there
/// are and what roles they hold.
/// <para>
/// Its own controller rather than more actions on <see cref="UsersController"/>,
/// the way the other services keep their aggregate reads: this is a count across
/// every account, the opposite of that controller's one-account-at-a-time shape.
/// </para>
/// </remarks>
[ApiController]
[Route("api/users/dashboard")]
[Authorize(Roles = PlatformRoles.Admin)]
public class DashboardController : ControllerBase
{
    private readonly IUserDashboardRepository _dashboardRepository;

    public DashboardController(IUserDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    /// <summary>
    /// The system-wide user counts (US-21 AC-4). Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// The total, how many accounts can sign in and how many have been
    /// deactivated, and the head count in every role — including roles nobody
    /// holds.
    /// </remarks>
    /// <response code="200">The user counts.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet("admin")]
    [ProducesResponseType(typeof(AdminUsersDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdminDashboard(CancellationToken cancellationToken)
    {
        var groups = await _dashboardRepository.CountByRoleAndStatusAsync(cancellationToken);

        return Ok(AdminUsersDashboardResponse.From(groups));
    }
}
