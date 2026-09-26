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
/// What <see cref="ClientPaymentHistoryController"/> answers with (US-17).
/// </summary>
public class ClientPaymentHistoryControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private readonly FakePaymentHistoryRepository _history = new();
    private readonly FakeProjectOwnerRepository _owners = new();

    [Fact]
    public async Task The_owning_client_sees_every_invoice_with_its_status()
    {
        // AC-1.
        _owners.GiveOwnership(ProjectId, ClientId);
        _history.GiveInvoice(ProjectId, 500m);
        _history.GiveInvoice(ProjectId, 300m, InvoiceStatus.Paid);

        var body = await HistoryFor(ClientId);

        Assert.Equal(2, body.Invoices.Count);
        Assert.Contains(body.Invoices, entry => entry.Invoice.Status == InvoiceStatus.Pending);
        Assert.Contains(body.Invoices, entry => entry.Invoice.Status == InvoiceStatus.Paid);
    }

    [Fact]
    public async Task Invoices_are_listed_most_recent_first()
    {
        // AC-1's ordering, preserved through the contract mapping rather than
        // only in the repository.
        _owners.GiveOwnership(ProjectId, ClientId);
        _history.GiveInvoice(ProjectId, 100m);
        _history.GiveInvoice(ProjectId, 200m);
        var newest = _history.GiveInvoice(ProjectId, 300m);

        var body = await HistoryFor(ClientId);

        Assert.Equal(newest, body.Invoices[0].Invoice.Id);
        Assert.Equal([300m, 200m, 100m], body.Invoices.Select(entry => entry.Invoice.Amount));
    }

    [Fact]
    public async Task Each_invoice_carries_its_own_payments()
    {
        // AC-1's "linked payments".
        _owners.GiveOwnership(ProjectId, ClientId);
        var billed = _history.GiveInvoice(ProjectId, 1_000m);
        _history.GiveInvoice(ProjectId, 400m);
        _history.GivePayment(billed, 250m);
        _history.GivePayment(billed, 150m);

        var body = await HistoryFor(ClientId);
        var entry = body.Invoices.Single(candidate => candidate.Invoice.Id == billed);

        Assert.Equal([250m, 150m], entry.Payments.Select(payment => payment.Amount));
        Assert.Equal(400m, entry.AmountPaid);
        Assert.Equal(600m, entry.OutstandingAmount);
    }

    [Fact]
    public async Task The_balance_is_returned_on_the_same_answer_as_the_history()
    {
        // AC-2: one request, so the balance cannot disagree with the invoices
        // beneath it.
        _owners.GiveOwnership(ProjectId, ClientId);
        var first = _history.GiveInvoice(ProjectId, 600m);
        _history.GiveInvoice(ProjectId, 400m);
        _history.GivePayment(first, 250m);

        var body = await HistoryFor(ClientId);

        Assert.Equal(1_000m, body.TotalInvoiced);
        Assert.Equal(250m, body.TotalPaid);
        Assert.Equal(750m, body.OutstandingBalance);
    }

    [Fact]
    public async Task A_project_with_no_invoices_is_an_empty_history_not_a_404()
    {
        // The project may well exist and simply not have been billed yet.
        _owners.GiveOwnership(ProjectId, ClientId);

        var body = await HistoryFor(ClientId);

        Assert.Empty(body.Invoices);
        Assert.Equal(0m, body.OutstandingBalance);
    }

    [Fact]
    public async Task A_fully_settled_project_owes_nothing()
    {
        _owners.GiveOwnership(ProjectId, ClientId);
        var invoice = _history.GiveInvoice(ProjectId, 500m, InvoiceStatus.Paid);
        _history.GivePayment(invoice, 500m);

        var body = await HistoryFor(ClientId);

        Assert.Equal(0m, body.OutstandingBalance);
        Assert.Equal(0m, body.Invoices[0].OutstandingAmount);
    }

    // --------------------------------------------------- the ownership gate ----

    [Fact]
    public async Task A_client_who_does_not_own_the_project_is_refused()
    {
        _owners.GiveOwnership(ProjectId, SomeoneElse);
        _history.GiveInvoice(ProjectId, 500m);

        var result = await ControllerFor(ClientId).Get(ProjectId, default);

        AssertForbidden(result);
        // And nothing was read — the gate runs before the repository.
        Assert.Empty(_history.Reads);
    }

    [Fact]
    public async Task A_project_whose_owner_is_not_yet_known_is_refused_rather_than_allowed()
    {
        // The ProjectCreated event may not have been consumed yet. Denying is
        // the safe direction.
        _history.GiveInvoice(ProjectId, 500m);

        AssertForbidden(await ControllerFor(ClientId).Get(ProjectId, default));
    }

    [Fact]
    public async Task A_project_that_is_not_theirs_looks_the_same_whether_or_not_it_has_invoices()
    {
        // So a Client probing ids cannot tell a billed project from an unbilled
        // one from one that does not exist.
        var billed = ProjectId;
        var unbilled = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000009");
        _owners.GiveOwnership(billed, SomeoneElse);
        _owners.GiveOwnership(unbilled, SomeoneElse);
        _history.GiveInvoice(billed, 500m);

        var onBilled = await ControllerFor(ClientId).Get(billed, default);
        var onUnbilled = await ControllerFor(ClientId).Get(unbilled, default);

        Assert.Equal(Status(onBilled), Status(onUnbilled));
        Assert.Equal(Title(onBilled), Title(onUnbilled));
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_is_refused()
    {
        _owners.GiveOwnership(ProjectId, ClientId);

        Assert.IsType<UnauthorizedResult>(await ControllerFor(callerId: null).Get(ProjectId, default));
        Assert.Empty(_history.Reads);
    }

    // ---------------------------------------------------------- helpers ----

    private async Task<PaymentHistoryResponse> HistoryFor(Guid clientId)
    {
        var result = await ControllerFor(clientId).Get(ProjectId, default);
        var ok = Assert.IsType<OkObjectResult>(result);

        return Assert.IsType<PaymentHistoryResponse>(ok.Value);
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
    private ClientPaymentHistoryController ControllerFor(Guid? callerId)
    {
        var claims = new List<Claim> { new("role", PlatformRoles.Client) };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ClientPaymentHistoryController(_history, _owners)
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
