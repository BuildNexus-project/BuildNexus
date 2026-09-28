using BuildNexus.ConstructionService.Authorization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ConstructionService.Controllers;

/// <summary>
/// Portfolio-wide reporting over the milestone data this service holds (US-19).
/// </summary>
/// <remarks>
/// Admin and Project Manager only: the story is framed around those two reading build
/// progress and financial health together. A Client sees their own project's progress
/// through <see cref="ConstructionProgressController"/> and has no business reading across
/// the portfolio.
/// <para>
/// Shaped after <c>design-service</c>'s own <c>ReportsController</c> — a route prefix of
/// <c>api/&lt;service&gt;/reports</c>, a role gate at the class level, and a dedicated
/// report repository rather than the request-path one. The difference is the role pair:
/// that report is Admin-only, this story names both.
/// </para>
/// <para>
/// This is one half of the combined report US-19 asks for. The payment half is Payment
/// Service's own endpoint over its own schema, and the frontend puts the two together —
/// no service reads another's database, which is what makes "one report" safe here.
/// </para>
/// </remarks>
[ApiController]
[Route("api/construction/reports")]
[Authorize(Roles = PlatformRoles.AdminOrProjectManager)]
public class ReportsController : ControllerBase
{
    private readonly IConstructionReportRepository _reports;

    public ReportsController(IConstructionReportRepository reports)
    {
        _reports = reports;
    }

    /// <summary>
    /// Per-project progress and milestone breakdown across the active projects that have
    /// milestones defined (AC-1). Allowed roles: Admin, ProjectManager.
    /// </summary>
    /// <remarks>
    /// "Active" is answered from this service's own state — a design-approved project
    /// whose build has not been handed over. See
    /// <see cref="IConstructionReportRepository.GetProgressAcrossActiveProjectsAsync"/>
    /// for exactly what that includes and the one case it cannot yet exclude.
    /// <para>
    /// Recomputed on every read from the milestones behind it, so the report cannot show
    /// a percentage the underlying rows do not support.
    /// </para>
    /// </remarks>
    /// <response code="200">The report, one row per active project with milestones, ordered by project id. An empty list when none have any — a real answer, not an error.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is neither an Admin nor a Project Manager.</response>
    [HttpGet("progress")]
    [ProducesResponseType(typeof(IReadOnlyList<ConstructionProgressReportRowResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProgressReport(CancellationToken cancellationToken)
    {
        var rows = await _reports.GetProgressAcrossActiveProjectsAsync(cancellationToken);

        return Ok(rows.Select(ConstructionProgressReportRowResponse.From).ToList());
    }
}
