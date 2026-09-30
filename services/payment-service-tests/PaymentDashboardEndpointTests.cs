using System.Security.Claims;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The payments half of US-21 AC-1 walked over a fake repository: a Client sees
/// what they still owe, and on which invoices.
/// </summary>
/// <remarks>
/// Who may call the endpoint is not decided in the action but by its role attribute,
/// so that is pinned in <see cref="EndpointRoleDeclarationTests"/>. The SQL — ownership
/// scoping, the Pending gate, the paid-so-far sum — is proved in
/// <see cref="PaymentDashboardRepositoryDatabaseTests"/>. What is under test here is the
/// endpoint's own decisions: whose id it scopes to, what it totals, and how the rows
/// reach the wire.
/// </remarks>
public class PaymentDashboardEndpointTests
{
    private static readonly Guid CallerId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid Villa = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid Cottage = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly DateTime Raised = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_client_sees_every_invoice_they_still_owe_on()
    {
        var (controller, repository) = ControllerWith();
        var first = Due(Villa, amount: 500m, paid: 0m);
        var second = Due(Cottage, amount: 1_000m, paid: 250m);
        repository.Due = [first, second];

        var dashboard = await Dashboard(controller);

        Assert.Equal(2, dashboard.InvoiceCount);
        Assert.Equal([first.InvoiceId, second.InvoiceId], dashboard.Invoices.Select(i => i.InvoiceId));
    }

    [Fact]
    public async Task The_total_due_is_what_is_still_outstanding_not_what_was_billed()
    {
        var (controller, repository) = ControllerWith();
        repository.Due =
        [
            Due(Villa, amount: 500m, paid: 0m),
            Due(Cottage, amount: 1_000m, paid: 250m)
        ];

        var dashboard = await Dashboard(controller);

        // 500 + (1000 - 250) — not 1500, which is billed, and not 250, which is paid.
        Assert.Equal(1_250m, dashboard.TotalDue);
    }

    [Fact]
    public async Task Each_invoice_carries_its_amount_what_is_paid_and_what_is_left()
    {
        var (controller, repository) = ControllerWith();
        var due = Due(Cottage, amount: 1_000m, paid: 250m);
        repository.Due = [due];

        var listed = Assert.Single((await Dashboard(controller)).Invoices);

        Assert.Equal(due.InvoiceId, listed.InvoiceId);
        Assert.Equal(Cottage, listed.ProjectId);
        Assert.Equal(1_000m, listed.Amount);
        Assert.Equal(250m, listed.AmountPaid);
        Assert.Equal(750m, listed.OutstandingAmount);
        Assert.Equal(Raised, listed.RaisedAt);
    }

    [Fact]
    public async Task Invoices_keep_the_order_the_repository_gave_them_in()
    {
        // Oldest first is the repository's decision; the endpoint must not reshuffle it.
        var (controller, repository) = ControllerWith();
        var oldest = Due(Villa, 100m, 0m);
        var newest = Due(Cottage, 300m, 0m);
        repository.Due = [oldest, newest];

        var dashboard = await Dashboard(controller);

        Assert.Equal([oldest.InvoiceId, newest.InvoiceId], dashboard.Invoices.Select(i => i.InvoiceId));
    }

    [Fact]
    public async Task A_client_who_owes_nothing_gets_a_zero_dashboard_not_an_error()
    {
        var (controller, _) = ControllerWith();

        var dashboard = await Dashboard(controller);

        Assert.Equal(0m, dashboard.TotalDue);
        Assert.Equal(0, dashboard.InvoiceCount);
        Assert.Empty(dashboard.Invoices);
    }

    [Fact]
    public async Task The_query_is_for_the_caller_named_in_the_token()
    {
        var (controller, repository) = ControllerWith();

        await controller.GetClientDashboard(default);

        Assert.Equal(CallerId, repository.LastClientId);
    }

    [Fact]
    public async Task A_token_with_no_usable_subject_is_refused_and_nothing_is_queried()
    {
        var (controller, repository) = ControllerWith(subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.GetClientDashboard(default));

        Assert.Equal(0, repository.Queries);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ClientPaymentsDashboardResponse> Dashboard(DashboardController controller) =>
        Assert.IsType<ClientPaymentsDashboardResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetClientDashboard(default)).Value);

    private static (DashboardController Controller, FakePaymentDashboardRepository Repository) ControllerWith(
        string? subject = null)
    {
        var repository = new FakePaymentDashboardRepository();

        var controller = new DashboardController(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The claims a token validated by this service leaves behind: "sub"
                    // and "role" in their short form, not rewritten into the
                    // WS-Federation URIs.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, subject ?? CallerId.ToString()),
                        new Claim("role", "Client")
                    ], "TestAuth"))
                }
            }
        };

        return (controller, repository);
    }

    private static PaymentDue Due(Guid projectId, decimal amount, decimal paid) => new()
    {
        InvoiceId = Guid.NewGuid(),
        ProjectId = projectId,
        Amount = amount,
        AmountPaid = paid,
        RaisedAtUtc = Raised
    };
}
