using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The read-only view a Client has of the invoices raised against their own
/// project (US-15).
/// </summary>
/// <remarks>
/// The story's purpose is that "the client always knows the expected and current
/// cost" — the quotation is the expected half, and these are the current half,
/// so the Client can see them for the same reason.
/// <para>
/// Ownership-scoped exactly as <see cref="ClientQuotationsController"/> is, and
/// kept a separate controller from <see cref="InvoicesController"/> so the two
/// role gates cannot be widened together.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.Client)]
public class ClientInvoicesController : ControllerBase
{
    private readonly IInvoiceRepository _invoices;
    private readonly IProjectOwnerRepository _owners;

    public ClientInvoicesController(
        IInvoiceRepository invoices,
        IProjectOwnerRepository owners)
    {
        _invoices = invoices;
        _owners = owners;
    }

    /// <summary>
    /// The invoices raised against the Client's own project, newest first.
    /// </summary>
    /// <response code="200">The project's invoices. A project that has not been billed is a real answer: an empty list.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client, or the project is not theirs.</response>
    [HttpGet("api/payments/my-projects/{projectId:guid}/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<InvoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId))
        {
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
                detail: "You can only view invoices for your own projects.");
        }

        var invoices = await _invoices.ListForProjectAsync(projectId, cancellationToken);

        return Ok(invoices.Select(InvoiceResponse.From).ToList());
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
