using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The read-only view a Client has of their own project's cost quotations
/// (US-15, AC-1 — "viewable by the Client").
/// </summary>
/// <remarks>
/// Client-only, and only for a project the caller owns. Deliberately a separate
/// controller rather than a widening of <see cref="QuotationsController"/>: that
/// one is ProjectManager/Admin at the class level and exists to generate the
/// estimate, whereas this only reads it. Keeping them apart means the two role
/// gates cannot be widened by accident together — the same split the
/// Construction Service makes between its milestone and progress controllers.
/// <para>
/// Nothing here writes. A Client is told what the build is expected to cost;
/// they do not set the figure.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.Client)]
public class ClientQuotationsController : ControllerBase
{
    private readonly IQuotationRepository _quotations;
    private readonly IProjectOwnerRepository _owners;

    public ClientQuotationsController(
        IQuotationRepository quotations,
        IProjectOwnerRepository owners)
    {
        _quotations = quotations;
        _owners = owners;
    }

    /// <summary>
    /// The Client's own project's quotations, newest first — so the first entry
    /// is what the project is currently expected to cost.
    /// </summary>
    /// <response code="200">The project's quotations. A project that has never been quoted is a real answer: an empty list, which the screen reads as "no estimate yet".</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client, or the project is not theirs.</response>
    [HttpGet("api/payments/my-projects/{projectId:guid}/quotations")]
    [ProducesResponseType(typeof(IReadOnlyList<QuotationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId))
        {
            // The role claim got the caller this far, but without a usable
            // subject there is no "their own project" to scope the read to.
            return Unauthorized();
        }

        // Ownership is checked before anything is read, and its failure is the
        // same 403 whatever the project's real state is. A Client guessing ids
        // therefore cannot tell a quoted project from an unquoted one from one
        // that does not exist at all — every answer that is not theirs looks
        // identical.
        if (!await _owners.IsOwnedByAsync(projectId, clientId, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your project.",
                detail: "You can only view quotations for your own projects.");
        }

        var quotations = await _quotations.ListForProjectAsync(projectId, cancellationToken);

        return Ok(quotations.Select(QuotationResponse.From).ToList());
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
