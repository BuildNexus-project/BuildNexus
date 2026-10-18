using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The exact words and links in <see cref="ClientPaymentsController"/>'s answers (US-16).
/// </summary>
/// <remarks>
/// Added for mutation testing (SE3112-04). <see cref="ClientPaymentsControllerTests"/>
/// already checks the status codes and the <c>reason</c> extension, but Stryker
/// showed that blanking any title or detail, or the controller name in the 201's
/// Location, broke no test. These are what the Client's screen shows, so they are
/// part of the contract.
/// </remarks>
public class ClientPaymentsControllerResponseTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid SomeoneElse = Guid.Parse("cccccccc-0000-4000-8000-000000000002");

    private readonly FakePaymentRepository _payments = new();
    private readonly FakeInvoiceRepository _invoices = new();
    private readonly FakeProjectOwnerRepository _owners = new();

    [Fact]
    public async Task A_recorded_payment_links_to_the_clients_invoice_listing_for_the_project()
    {
        var invoice = SeedInvoice(ownerId: ClientId, amount: 1_000m, payable: true);

        var result = await Controller().Record(invoice, new() { Amount = 100m }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal("ClientInvoices", created.ControllerName);
        Assert.Equal(nameof(ClientInvoicesController.List), created.ActionName);
        Assert.Equal(ProjectId, created.RouteValues!["projectId"]);
    }

    [Fact]
    public async Task Someone_elses_invoice_is_refused_with_a_message_saying_it_is_not_theirs()
    {
        var invoice = SeedInvoice(ownerId: SomeoneElse, amount: 1_000m, payable: true);

        var problem = Problem(await Controller().Record(invoice, new() { Amount = 100m }, default));

        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("Not your invoice.", problem.Title);
        Assert.Equal("You can only record payments against invoices on your own projects.", problem.Detail);
    }

    [Fact]
    public async Task An_invoice_deleted_between_the_two_reads_gets_the_same_403_as_someone_elses()
    {
        // The controller reads the invoice and finds it, but by the time the payment
        // is recorded it is gone. That race must not reveal anything new to the caller.
        var invoice = SeedInvoice(ownerId: ClientId, amount: 1_000m, payable: false);

        var problem = Problem(await Controller().Record(invoice, new() { Amount = 100m }, default));

        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("Not your invoice.", problem.Title);
        Assert.Equal("You can only record payments against invoices on your own projects.", problem.Detail);
    }

    [Fact]
    public async Task Paying_too_much_says_so_and_tells_the_client_what_to_enter()
    {
        var invoice = SeedInvoice(ownerId: ClientId, amount: 250m, payable: true);

        var problem = Problem(await Controller().Record(invoice, new() { Amount = 300m }, default));

        Assert.Equal("Payment exceeds the outstanding amount.", problem.Title);
        Assert.Equal("This invoice has 250.00 outstanding. Enter that amount or less.", problem.Detail);
    }

    [Fact]
    public async Task Paying_a_settled_invoice_says_there_is_nothing_left_to_pay()
    {
        var invoice = SeedInvoice(ownerId: ClientId, amount: 250m, payable: true);
        var controller = Controller();
        await controller.Record(invoice, new() { Amount = 250m }, default);

        var problem = Problem(await controller.Record(invoice, new() { Amount = 1m }, default));

        Assert.Equal("This invoice is already paid.", problem.Title);
        Assert.Equal(
            "Nothing is outstanding on this invoice, so there is nothing left to pay.",
            problem.Detail);
    }

    // ---------------------------------------------------------- helpers ----

    /// <summary>
    /// An invoice the controller can read. With <paramref name="payable"/> false the
    /// payment repository does not know it, which is how the deleted-in-between race
    /// looks from the controller.
    /// </summary>
    private Guid SeedInvoice(Guid ownerId, decimal amount, bool payable)
    {
        var invoiceId = Guid.NewGuid();
        _invoices.Seed(new Invoice
        {
            Id = invoiceId,
            ProjectId = ProjectId,
            Amount = amount,
            Status = InvoiceStatus.Pending,
            CreatedBy = Guid.NewGuid(),
            CreatedAtUtc = new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc)
        });
        _owners.GiveOwnership(ProjectId, ownerId);

        if (payable)
        {
            _payments.GiveInvoice(invoiceId, amount);
        }

        return invoiceId;
    }

    private static ProblemDetails Problem(IActionResult result) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);

    private ClientPaymentsController Controller() =>
        new(_payments, _invoices, _owners)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim("role", PlatformRoles.Client),
                            new Claim(JwtRegisteredClaimNames.Sub, ClientId.ToString())
                        ],
                        "TestAuth"))
                }
            }
        };
}
