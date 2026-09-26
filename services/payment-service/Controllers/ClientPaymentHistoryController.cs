using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The Client's own billing view: what they have been invoiced, what they have
/// paid, and what they still owe (US-17).
/// </summary>
/// <remarks>
/// Client-only, and only for a project the caller owns — the same ownership gate
/// the other Client-facing reads use. Kept a separate controller from
/// <see cref="ClientInvoicesController"/> for the reason the others are kept
/// apart: one role gate per screen, so widening one cannot widen another by
/// accident.
/// <para>
/// Nothing here writes. Every figure is derived on the read from the rows US-15
/// and US-16 already store, so this story adds no column and no stored total.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.Client)]
public class ClientPaymentHistoryController : ControllerBase
{
    private readonly IPaymentHistoryRepository _history;
    private readonly IProjectOwnerRepository _owners;

    public ClientPaymentHistoryController(
        IPaymentHistoryRepository history,
        IProjectOwnerRepository owners)
    {
        _history = history;
        _owners = owners;
    }

    /// <summary>
    /// The project's invoices with their payments, most recent first, and the
    /// balance still outstanding across them (AC-1 and AC-2).
    /// </summary>
    /// <remarks>
    /// Answers from a fresh read every time — no caching, no stored rollup — so
    /// a caller that re-reads after recording a payment sees the new balance
    /// (AC-2). How often to re-read is the screen's decision.
    /// </remarks>
    /// <response code="200">The history and the balance. A project with no invoices is a real answer: an empty list at a zero balance.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client, or the project is not theirs.</response>
    [HttpGet("api/payments/my-projects/{projectId:guid}/history")]
    [ProducesResponseType(typeof(PaymentHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId))
        {
            // The role claim got the caller this far, but without a usable
            // subject there is no "their own project" to scope the read to.
            return Unauthorized();
        }

        // Checked before anything is read, and the refusal is identical whatever
        // the project's real state is — so a Client guessing ids cannot tell a
        // billed project from an unbilled one from one that does not exist.
        if (!await _owners.IsOwnedByAsync(projectId, clientId, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your project.",
                detail: "You can only view the payment history for your own projects.");
        }

        var history = await _history.GetForProjectAsync(projectId, cancellationToken);

        return Ok(PaymentHistoryResponse.From(history));
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
