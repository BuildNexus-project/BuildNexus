using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// Portfolio-wide reporting over the invoices and payments this service holds (US-19).
/// </summary>
/// <remarks>
/// Project Manager and Admin — the same pair that raises invoices, and the two roles US-19
/// names. A Client reads their own billing through
/// <see cref="ClientPaymentHistoryController"/> and has no business reading the portfolio's
/// financial health; an Architect has no part in billing at all.
/// <para>
/// This is one half of the combined report. The construction half is Construction Service's
/// own endpoint over its own schema, and the frontend puts the two together — which is what
/// makes "one report" possible without either service reading the other's database.
/// </para>
/// </remarks>
[ApiController]
[Route("api/payments/reports")]
[Authorize(Roles = $"{PlatformRoles.ProjectManager},{PlatformRoles.Admin}")]
public class ReportsController : ControllerBase
{
    private readonly IPaymentReportRepository _reports;

    public ReportsController(IPaymentReportRepository reports)
    {
        _reports = reports;
    }

    /// <summary>
    /// Invoiced, collected and outstanding totals across every project, optionally narrowed
    /// to a date range (AC-2). Allowed roles: ProjectManager, Admin.
    /// </summary>
    /// <remarks>
    /// <paramref name="fromUtc"/> is inclusive and <paramref name="toUtc"/> exclusive, so
    /// two adjacent ranges neither overlap nor lose a row between them. Both are optional
    /// and independent — a caller may give either, both, or neither.
    /// <para>
    /// With either bound given, <c>totalOutstanding</c> comes back null: invoiced is scoped
    /// by when an invoice was raised and collected by when a payment was recorded, so their
    /// difference inside a window can be negative and means nothing. "What is still owed" is
    /// a question about the whole ledger.
    /// </para>
    /// </remarks>
    /// <response code="200">The totals. Zeros for an empty ledger — a real answer, not an error.</response>
    /// <response code="400">The range was inverted: <c>fromUtc</c> is not before <c>toUtc</c>.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is neither a Project Manager nor an Admin.</response>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(PaymentReportSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        // An inverted range is a caller mistake, and it would silently answer zeros — which
        // reads as "nothing was billed" rather than "you asked for an empty window". Refusing
        // it names the mistake instead.
        if (fromUtc is not null && toUtc is not null && fromUtc >= toUtc)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid date range.",
                detail: "fromUtc must be earlier than toUtc.");
        }

        var summary = await _reports.GetSummaryAsync(fromUtc, toUtc, cancellationToken);

        return Ok(PaymentReportSummaryResponse.From(summary));
    }
}
