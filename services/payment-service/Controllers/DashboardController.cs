using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The payments half of the Client's dashboard (US-21). Answers only from this
/// service's own database.
/// </summary>
/// <remarks>
/// A dashboard needs data from all five services, and each service's database is
/// its own. So there is no single "dashboard" call: every service exposes the slice
/// it owns, under its own gateway prefix, and the page joins them. What belongs here
/// is what only the Payment Service knows — what a Client has been billed and what
/// they have paid.
/// <para>
/// Its own controller rather than another action on <see cref="ClientPaymentHistoryController"/>:
/// that one answers for a single project the caller names and checks they own it,
/// while this spans every project they own without naming any. Nothing here writes.
/// </para>
/// <para>
/// The class carries a bare <c>[Authorize]</c> and the action names its role, the
/// same arrangement the other services' dashboards use.
/// </para>
/// </remarks>
[ApiController]
[Route("api/payments/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IPaymentDashboardRepository _dashboardRepository;

    public DashboardController(IPaymentDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    /// <summary>
    /// What the Client still owes, and on which invoices (US-21 AC-1). Allowed roles:
    /// Client.
    /// </summary>
    /// <remarks>
    /// Scoped to the caller by this service's own record of who owns each project,
    /// and the endpoint takes no id — a Client cannot ask for another Client's
    /// dashboard because there is nowhere to say whose. Every unsettled invoice is
    /// listed, oldest first, with what has been paid against it; the total is the sum
    /// of what is still outstanding on them. A settled invoice is not due.
    /// </remarks>
    /// <response code="200">The invoices still owing and the total. An empty list at a zero total when nothing is owing — a real answer, not an error.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client.</response>
    [HttpGet("client")]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ClientPaymentsDashboardResponse), StatusCodes.Status200OK)]
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

        var due = await _dashboardRepository.ListPaymentsDueForClientAsync(clientId, cancellationToken);

        return Ok(ClientPaymentsDashboardResponse.From(due));
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
