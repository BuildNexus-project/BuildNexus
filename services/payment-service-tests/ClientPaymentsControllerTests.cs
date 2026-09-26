using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="ClientPaymentsController"/> does with a request, over fakes
/// (US-16).
/// </summary>
public class ClientPaymentsControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private readonly FakePaymentRepository _payments = new();
    private readonly FakeInvoiceRepository _invoices = new();
    private readonly FakeProjectOwnerRepository _owners = new();

    [Fact]
    public async Task A_recorded_payment_answers_with_the_new_balance_and_status()
    {
        var invoice = await OwnedInvoice(1_000m);

        var result = await ControllerFor(ClientId).Record(invoice, Request(400m), default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<RecordPaymentResponse>(created.Value);

        Assert.Equal(400m, body.Payment.Amount);
        Assert.Equal(600m, body.OutstandingAmount);
        Assert.Equal(InvoiceStatus.Pending, body.InvoiceStatus);
    }

    [Fact]
    public async Task The_payment_that_settles_the_invoice_reports_it_as_Paid()
    {
        // AC-2, as the Client's screen sees it — without a second read.
        var invoice = await OwnedInvoice(1_000m);
        var controller = ControllerFor(ClientId);
        await controller.Record(invoice, Request(400m), default);

        var result = await controller.Record(invoice, Request(600m), default);

        var body = Assert.IsType<RecordPaymentResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(0m, body.OutstandingAmount);
        Assert.Equal(InvoiceStatus.Paid, body.InvoiceStatus);
    }

    [Fact]
    public async Task The_payment_is_attributed_to_the_caller_and_filed_against_the_route_invoice()
    {
        var invoice = await OwnedInvoice(500m);

        await ControllerFor(ClientId).Record(invoice, Request(100m), default);

        var attempt = Assert.Single(_payments.Attempts);
        Assert.Equal(ClientId, attempt.PaidBy);
        Assert.Equal(invoice, attempt.InvoiceId);
    }

    // ------------------------------------------------- AC-1: the cap ----

    [Fact]
    public async Task A_payment_over_the_outstanding_amount_is_a_conflict_naming_the_reason()
    {
        // AC-1. A 409 rather than a 400: the request was well formed and the
        // caller was allowed — what refused it is the state of the invoice.
        var invoice = await OwnedInvoice(1_000m);

        var result = await ControllerFor(ClientId).Record(invoice, Request(1_500m), default);

        var problem = AssertConflict(result);
        Assert.Equal("ExceedsOutstanding", problem.Extensions["reason"]);
    }

    [Fact]
    public async Task A_refused_payment_reports_what_may_actually_be_paid()
    {
        // So the screen can offer to correct the figure rather than only report
        // the error.
        var invoice = await OwnedInvoice(1_000m);
        var controller = ControllerFor(ClientId);
        await controller.Record(invoice, Request(400m), default);

        var problem = AssertConflict(await controller.Record(invoice, Request(700m), default));

        Assert.Equal(600m, problem.Extensions["outstandingAmount"]);
        Assert.Contains("600.00", problem.Detail);
    }

    [Fact]
    public async Task Paying_a_settled_invoice_says_so_rather_than_blaming_the_amount()
    {
        var invoice = await OwnedInvoice(300m);
        var controller = ControllerFor(ClientId);
        await controller.Record(invoice, Request(300m), default);

        var problem = AssertConflict(await controller.Record(invoice, Request(1m), default));

        Assert.Equal("AlreadyPaid", problem.Extensions["reason"]);
    }

    // --------------------------------------------------- the ownership gate ----

    [Fact]
    public async Task A_client_cannot_pay_an_invoice_on_someone_elses_project()
    {
        var invoice = await InvoiceOwnedBy(SomeoneElse, 1_000m);

        var result = await ControllerFor(ClientId).Record(invoice, Request(100m), default);

        AssertForbidden(result);
        // And nothing was even attempted — the gate runs before the repository.
        Assert.Empty(_payments.Attempts);
    }

    [Fact]
    public async Task An_invoice_that_does_not_exist_looks_the_same_as_one_that_is_not_theirs()
    {
        // So a Client cannot use this endpoint to discover which invoices exist —
        // which matters more here than on a read, because this one writes.
        var mine = await InvoiceOwnedBy(SomeoneElse, 1_000m);

        var onSomeoneElses = await ControllerFor(ClientId).Record(mine, Request(100m), default);
        var onNothing = await ControllerFor(ClientId).Record(Guid.NewGuid(), Request(100m), default);

        Assert.Equal(Status(onSomeoneElses), Status(onNothing));
        Assert.Equal(Title(onSomeoneElses), Title(onNothing));
    }

    [Fact]
    public async Task A_project_whose_owner_is_not_yet_known_is_refused_rather_than_allowed()
    {
        // The ProjectCreated event may not have been consumed yet. Denying is the
        // safe direction when the endpoint takes money.
        var invoice = Guid.NewGuid();
        _invoices.Seed(Invoice(invoice, ProjectId, 1_000m));
        _payments.GiveInvoice(invoice, 1_000m);

        AssertForbidden(await ControllerFor(ClientId).Record(invoice, Request(100m), default));
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_cannot_record_a_payment()
    {
        var invoice = await OwnedInvoice(1_000m);

        Assert.IsType<UnauthorizedResult>(
            await ControllerFor(callerId: null).Record(invoice, Request(100m), default));
        Assert.Empty(_payments.Attempts);
    }

    [Fact]
    public async Task An_invalid_amount_is_refused_before_anything_is_attempted()
    {
        var invoice = await OwnedInvoice(1_000m);
        var controller = ControllerFor(ClientId);
        controller.ModelState.AddModelError(
            nameof(RecordPaymentRequest.Amount), "The amount must be greater than zero.");

        var result = await controller.Record(invoice, Request(0m), default);

        var details = Assert.IsType<ValidationProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Contains(nameof(RecordPaymentRequest.Amount), details.Errors.Keys);
        Assert.Empty(_payments.Attempts);
    }

    // ---------------------------------------------------------- helpers ----

    private static RecordPaymentRequest Request(decimal amount) => new() { Amount = amount };

    /// <summary>An invoice on a project the signed-in Client owns.</summary>
    private Task<Guid> OwnedInvoice(decimal amount) => InvoiceOwnedBy(ClientId, amount);

    private Task<Guid> InvoiceOwnedBy(Guid ownerId, decimal amount)
    {
        var invoiceId = Guid.NewGuid();
        _invoices.Seed(Invoice(invoiceId, ProjectId, amount));
        _payments.GiveInvoice(invoiceId, amount);
        _owners.GiveOwnership(ProjectId, ownerId);

        return Task.FromResult(invoiceId);
    }

    private static Invoice Invoice(Guid id, Guid projectId, decimal amount) => new()
    {
        Id = id,
        ProjectId = projectId,
        Amount = amount,
        Status = InvoiceStatus.Pending,
        CreatedBy = Guid.NewGuid(),
        CreatedAtUtc = new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc),
        PaidAtUtc = null
    };

    private static ProblemDetails AssertConflict(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        return problem;
    }

    private static void AssertForbidden(IActionResult result) =>
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status);

    private static int? Status(IActionResult result) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status;

    private static string? Title(IActionResult result) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Title;

    /// <summary>
    /// The controller with a signed-in Client on it. <paramref name="callerId"/>
    /// of <c>null</c> stands for a token that carried no usable <c>sub</c>.
    /// </summary>
    private ClientPaymentsController ControllerFor(Guid? callerId)
    {
        var claims = new List<Claim> { new("role", PlatformRoles.Client) };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ClientPaymentsController(_payments, _invoices, _owners)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            }
        };
    }
}
