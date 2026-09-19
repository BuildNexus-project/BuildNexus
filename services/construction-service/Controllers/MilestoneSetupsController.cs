using BuildNexus.ConstructionService.Authorization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ConstructionService.Controllers;

/// <summary>
/// The milestone-setup placeholders the <c>DesignApproved</c> consumer has
/// created (US-23). Read-only for now: the placeholders are written by the
/// consumer, not by a request. Admin-only, so a run can be checked without a
/// project role.
/// </summary>
[ApiController]
[Route("api/construction/milestone-setups")]
[Authorize(Roles = PlatformRoles.Admin)]
public class MilestoneSetupsController : ControllerBase
{
    private readonly IMilestoneSetupRepository _repository;

    public MilestoneSetupsController(IMilestoneSetupRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Every milestone-setup placeholder, newest first — one per project whose
    /// design has been approved.
    /// Allowed roles: Admin.
    /// </summary>
    /// <response code="200">The placeholders, newest first.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MilestoneSetupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var setups = await _repository.ListAsync(cancellationToken);

        return Ok(setups.Select(MilestoneSetupResponse.From).ToList());
    }
}
