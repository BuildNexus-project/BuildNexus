using System.Security.Claims;
using BuildNexus.ConstructionService.Authorization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Controllers;

/// <summary>
/// The read-only view a Client has of their own project's construction
/// progress (US-13).
/// </summary>
/// <remarks>
/// Client-only, and only for a project the caller owns. Deliberately a separate
/// controller rather than a widening of <see cref="MilestonesController"/>:
/// that one is Project-Manager-only at the class level and every endpoint on it
/// writes or is framed around planning the build, whereas this reads and is
/// framed around watching it. Keeping them apart means US-12's contract does
/// not move, and the two role gates cannot be widened by accident together.
/// <para>
/// Nothing here writes. The data is the same milestone rows US-12 already
/// stores, and the percentage is recomputed on every read from those rows —
/// this story adds no column and stores no snapshot.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.Client)]
public class ConstructionProgressController : ControllerBase
{
    private readonly IMilestoneRepository _milestones;
    private readonly IProjectOwnerRepository _owners;

    public ConstructionProgressController(
        IMilestoneRepository milestones,
        IProjectOwnerRepository owners)
    {
        _milestones = milestones;
        _owners = owners;
    }

    /// <summary>
    /// How the Client's project is advancing: every milestone with its current
    /// status, and the overall completion percentage (AC-1).
    /// </summary>
    /// <remarks>
    /// Answers from a fresh read every time — no caching, no stored rollup — so
    /// a caller that re-reads after a status change sees the change (AC-2). The
    /// dashboard is what decides how often to re-read.
    /// </remarks>
    /// <response code="200">The milestones and the rollup. A project with none defined yet is a real answer: an empty list at zero percent.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client, or the project is not theirs.</response>
    /// <response code="404">The project's design has not yet been approved, so it has no construction plan to report on.</response>
    [HttpGet("api/construction/projects/{projectId:guid}/progress-summary")]
    [ProducesResponseType(typeof(ProjectProgressSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId))
        {
            // The role claim got the caller this far, but without a usable
            // subject there is no "their own project" to scope the read to.
            return Unauthorized();
        }

        // Ownership is checked before anything is read, and its failure is the
        // same 403 whatever the project's real state is. A Client guessing ids
        // therefore cannot tell an approved project from an unapproved one from
        // one that does not exist at all — every answer that is not theirs looks
        // identical.
        if (!await _owners.IsOwnedByAsync(projectId, clientId, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your project.",
                detail: "You can only view construction progress for your own projects.");
        }

        var progress = await _milestones.GetProgressForProjectAsync(projectId, cancellationToken);

        if (progress is null)
        {
            // No milestone_setups row — the design has not been approved, so
            // there is no construction plan yet. The same 404 the PM's own
            // progress endpoint gives, for the same reason: reporting "0%" for
            // a plan that does not exist would tell the Client their build has
            // started and stalled.
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Progress not available.",
                detail: "This project's design has not yet been approved, so construction has not been planned yet.");
        }

        // Read after the rollup, not before. Both come from the same rows and
        // neither is a snapshot, so the worst a concurrent status change can do
        // in this order is show a milestone as Completed while the percentage
        // still counts it as pending — an understatement the next poll
        // corrects. The other order would overstate, showing a percentage the
        // visible milestones do not account for.
        var milestones = await _milestones.ListForProjectAsync(projectId, cancellationToken);

        return Ok(ProjectProgressSummaryResponse.From(progress, milestones));
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
