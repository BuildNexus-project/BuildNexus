using BuildNexus.DesignService.Authorization;
using BuildNexus.DesignService.Contracts;
using BuildNexus.DesignService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.DesignService.Controllers;

/// <summary>
/// Admin reporting over the design documents this service holds. Every action
/// requires a valid bearer token and is Admin-only — the architects being
/// measured and the clients whose projects are counted do not see these.
/// </summary>
[ApiController]
[Route("api/designs/reports")]
[Authorize(Roles = PlatformRoles.Admin)]
public class ReportsController : ControllerBase
{
    private readonly IDesignReportRepository _reportRepository;

    public ReportsController(IDesignReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    /// <summary>
    /// The design approval report (US-20): one row per project that has any
    /// design document, with its revision volume and time-to-approval.
    /// Allowed roles: Admin.
    /// </summary>
    /// <response code="200">The report, one row per project, ordered by project id.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet("approval")]
    [ProducesResponseType(typeof(IReadOnlyList<DesignApprovalReportRowResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApprovalReport(CancellationToken cancellationToken)
    {
        var rows = await _reportRepository.GetApprovalReportAsync(cancellationToken);

        return Ok(rows.Select(DesignApprovalReportRowResponse.From).ToList());
    }
}
