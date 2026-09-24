using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The cost estimates a Project Manager or Admin draws up for a project
/// (US-15, AC-1).
/// </summary>
/// <remarks>
/// Project Manager and Admin, on every endpoint here: the story is framed around
/// those two roles generating the estimate, and an Architect has no part in
/// pricing a build. The Client's own view of these same quotations is a separate,
/// read-only, ownership-scoped controller — kept apart so widening the role gate
/// on one cannot silently widen it on the other.
/// </remarks>
[ApiController]
[Authorize(Roles = $"{PlatformRoles.ProjectManager},{PlatformRoles.Admin}")]
public class QuotationsController : ControllerBase
{
    private readonly IQuotationRepository _quotations;

    public QuotationsController(IQuotationRepository quotations)
    {
        _quotations = quotations;
    }

    /// <summary>
    /// Generates a cost quotation for a project, linked to it and storing the
    /// estimated total (AC-1).
    /// </summary>
    /// <remarks>
    /// Always creates a new quotation rather than replacing the project's
    /// existing one. A project can be re-quoted as its scope firms up, and the
    /// earlier figures are the record of what the Client was told before.
    /// <para>
    /// The project id is recorded as given. Projects belong to the Project
    /// Service and this service may not query its database; a synchronous check
    /// would make quoting fail whenever that service is down, for a fact that
    /// does not change once it is true.
    /// </para>
    /// </remarks>
    /// <response code="201">The quotation as stored.</response>
    /// <response code="400">The estimated total was missing, zero, negative, or larger than the column holds.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Project Manager or an Admin.</response>
    [HttpPost("api/payments/projects/{projectId:guid}/quotations")]
    [ProducesResponseType(typeof(QuotationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Generate(
        Guid projectId,
        [FromBody] GenerateQuotationRequest request,
        CancellationToken cancellationToken)
    {
        // ModelState covers [Required] and [Range] — a zero, negative or
        // over-large total is refused before the repository is touched.
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCallerId(out var createdBy))
        {
            // The role claim got the caller this far, but a quotation records who
            // made it, and a token with no usable subject cannot answer that.
            return Unauthorized();
        }

        var quotation = await _quotations.CreateAsync(
            projectId,
            request.EstimatedTotal,
            createdBy,
            cancellationToken);

        // CreatedAtAction points at the project's listing rather than a
        // per-quotation GET — the story does not need one, and inventing an
        // endpoint just to fill Location would exceed scope.
        return CreatedAtAction(
            nameof(List),
            new { projectId },
            QuotationResponse.From(quotation));
    }

    /// <summary>
    /// Every quotation raised for a project, newest first — so the first entry is
    /// the project's current estimate and the rest are its history.
    /// </summary>
    /// <response code="200">The project's quotations. A project that has never been quoted is a real answer: an empty list.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager or an Admin.</response>
    [HttpGet("api/payments/projects/{projectId:guid}/quotations")]
    [ProducesResponseType(typeof(IReadOnlyList<QuotationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var quotations = await _quotations.ListForProjectAsync(projectId, cancellationToken);

        return Ok(quotations.Select(QuotationResponse.From).ToList());
    }

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
