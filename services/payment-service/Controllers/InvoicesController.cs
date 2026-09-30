using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The invoices raised against a project at its billable stages (US-15, AC-2).
/// </summary>
/// <remarks>
/// Project Manager and Admin, the same two roles that generate a quotation: an
/// Architect has no part in billing, and a Client is billed rather than billing.
/// The Client's own read-only view of their invoices lives on
/// <see cref="ClientInvoicesController"/>, scoped to projects they own.
/// <para>
/// This is the manual half of AC-2. The automatic half — an invoice raised off a
/// <c>ConstructionStarted</c> event — goes through the same repository, so both
/// paths produce the same kind of row.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = $"{PlatformRoles.ProjectManager},{PlatformRoles.Admin}")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceRepository _invoices;

    public InvoicesController(IInvoiceRepository invoices)
    {
        _invoices = invoices;
    }

    /// <summary>
    /// Raises an invoice against a project: a unique id, an amount, and a status
    /// of Pending (AC-2).
    /// </summary>
    /// <response code="201">The invoice as stored.</response>
    /// <response code="400">The amount was missing, zero, negative, or larger than the column holds.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Project Manager or an Admin.</response>
    [HttpPost("api/payments/projects/{projectId:guid}/invoices")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Generate(
        Guid projectId,
        [FromBody] GenerateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCallerId(out var createdBy))
        {
            // The role claim got the caller this far, but a bill records who
            // raised it, and a token with no usable subject cannot answer that.
            return Unauthorized();
        }

        var invoice = await _invoices.CreateAsync(projectId, request.Amount, createdBy, cancellationToken);

        return CreatedAtAction(nameof(List), new { projectId }, InvoiceResponse.From(invoice));
    }

    /// <summary>
    /// Every invoice raised against a project, newest first.
    /// </summary>
    /// <response code="200">The project's invoices. A project that has not been billed is a real answer: an empty list.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager or an Admin.</response>
    [HttpGet("api/payments/projects/{projectId:guid}/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<InvoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var invoices = await _invoices.ListForProjectAsync(projectId, cancellationToken);

        return Ok(invoices.Select(InvoiceResponse.From).ToList());
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
