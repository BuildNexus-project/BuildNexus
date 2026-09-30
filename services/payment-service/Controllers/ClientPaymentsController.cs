using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Controllers;

/// <summary>
/// The payments a Client records against their own project's invoices (US-16).
/// </summary>
/// <remarks>
/// Client-only, and only for a project the caller owns. The story is framed
/// around the Client paying down their own balance; staff raise the invoice
/// (US-15) and do not pay it on the customer's behalf.
/// <para>
/// Ownership-scoped exactly as the Client's quotation and invoice views are, and
/// kept a separate controller from <see cref="InvoicesController"/> so the two
/// role gates cannot be widened together.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.Client)]
public class ClientPaymentsController : ControllerBase
{
    private readonly IPaymentRepository _payments;
    private readonly IInvoiceRepository _invoices;
    private readonly IProjectOwnerRepository _owners;

    public ClientPaymentsController(
        IPaymentRepository payments,
        IInvoiceRepository invoices,
        IProjectOwnerRepository owners)
    {
        _payments = payments;
        _invoices = invoices;
        _owners = owners;
    }

    /// <summary>
    /// Records a payment against an invoice, reducing the outstanding balance.
    /// </summary>
    /// <remarks>
    /// The amount may not exceed what is still outstanding (AC-1), and the
    /// payment that takes the balance to zero settles the invoice (AC-2). Both
    /// are decided inside one transaction holding a lock on the invoice, so two
    /// payments at once cannot both be allowed against the same balance.
    /// </remarks>
    /// <response code="201">The payment, with the invoice's new outstanding amount and status.</response>
    /// <response code="400">The amount was missing, zero, negative, or larger than the column holds.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Client, or the invoice is not on one of their projects.</response>
    /// <response code="409">The amount exceeds the outstanding balance, or the invoice is already settled. The <c>reason</c> extension says which, and <c>outstandingAmount</c> says what may actually be paid.</response>
    [HttpPost("api/payments/invoices/{invoiceId:guid}/payments")]
    [ProducesResponseType(typeof(RecordPaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Record(
        Guid invoiceId,
        [FromBody] RecordPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCallerId(out var clientId))
        {
            // The role claim got the caller this far, but a payment records who
            // made it, and a token with no usable subject cannot answer that.
            return Unauthorized();
        }

        var invoice = await _invoices.GetAsync(invoiceId, cancellationToken);

        // An unknown invoice and somebody else's invoice answer identically. A
        // Client guessing ids therefore cannot use this endpoint to discover
        // which invoices exist — the same reasoning US-15's Client reads follow,
        // and it matters more here because this one writes.
        if (invoice is null || !await _owners.IsOwnedByAsync(invoice.ProjectId, clientId, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your invoice.",
                detail: "You can only record payments against invoices on your own projects.");
        }

        var result = await _payments.RecordPaymentAsync(invoiceId, request.Amount, clientId, cancellationToken);

        return result.Outcome switch
        {
            PaymentRecordingOutcome.Recorded => Created(
                invoice.ProjectId,
                RecordPaymentResponse.From(result.Payment!, result.OutstandingAmount, result.InvoiceStatus!.Value)),

            // The invoice was there a moment ago — it can only have gone if it
            // was deleted between the two reads. Answered as the same refusal as
            // an invoice that was never theirs, rather than leaking the race.
            PaymentRecordingOutcome.InvoiceNotFound => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your invoice.",
                detail: "You can only record payments against invoices on your own projects."),

            _ => Refused(result)
        };
    }

    /// <summary>
    /// The 201, pointing at the invoice the payment was made against.
    /// </summary>
    /// <remarks>
    /// There is no per-payment GET — the story does not need one, and inventing
    /// an endpoint just to fill <c>Location</c> would exceed scope. The Client's
    /// own invoice listing is where the payment becomes visible.
    /// </remarks>
    private IActionResult Created(Guid projectId, RecordPaymentResponse response) =>
        CreatedAtAction(
            actionName: nameof(ClientInvoicesController.List),
            controllerName: "ClientInvoices",
            routeValues: new { projectId },
            value: response);

    /// <summary>
    /// A refusal the invoice's own state caused (AC-1, and an already-settled
    /// invoice).
    /// </summary>
    /// <remarks>
    /// Always a 409: the request was well formed and the caller was allowed —
    /// what refused it is the state of the invoice, which is what a conflict
    /// means. The specific reason travels three ways: <c>detail</c> is the
    /// sentence the Client reads, the <c>reason</c> extension carries the
    /// outcome's own name so a caller can branch without matching on English
    /// prose, and <c>outstandingAmount</c> says what may actually be paid — so a
    /// screen can offer to correct the figure rather than only report the error.
    /// </remarks>
    private IActionResult Refused(PaymentRecordingResult result)
    {
        var (title, detail) = Describe(result);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail
        };

        problem.Extensions["reason"] = result.Outcome.ToString();
        problem.Extensions["outstandingAmount"] = result.OutstandingAmount;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }

    /// <summary>The sentence each refusal shows the Client.</summary>
    /// <remarks>
    /// Written to say what to do next rather than only what went wrong, and
    /// naming the figure — a Client who typed too much wants to know the number
    /// that would have worked.
    /// </remarks>
    private static (string Title, string Detail) Describe(PaymentRecordingResult result) =>
        result.Outcome switch
        {
            PaymentRecordingOutcome.ExceedsOutstanding => (
                "Payment exceeds the outstanding amount.",
                $"This invoice has {result.OutstandingAmount:0.00} outstanding. "
                + "Enter that amount or less."),

            PaymentRecordingOutcome.AlreadyPaid => (
                "This invoice is already paid.",
                "Nothing is outstanding on this invoice, so there is nothing left to pay."),

            _ => (
                "Payment could not be recorded.",
                "The invoice is not in a state where a payment can be recorded against it.")
        };

    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
