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
/// What <see cref="InvoicesController"/> and
/// <see cref="ClientInvoicesController"/> do with a request, over fakes
/// (US-15, AC-2).
/// </summary>
public class InvoicesControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("dddddddd-0000-4000-8000-000000000001");

    private readonly FakeInvoiceRepository _invoices = new();
    private readonly FakeProjectOwnerRepository _owners = new();

    [Fact]
    public async Task A_generated_invoice_has_a_unique_id_an_amount_and_a_pending_status()
    {
        // AC-2, the whole bullet, as the API hands it back.
        var result = await StaffController(ManagerId)
            .Generate(ProjectId, new GenerateInvoiceRequest { Amount = 250_000.75m }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<InvoiceResponse>(created.Value);

        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal(250_000.75m, body.Amount);
        Assert.Equal(InvoiceStatus.Pending, body.Status);
        Assert.Null(body.PaidAtUtc);
    }

    [Fact]
    public async Task The_caller_cannot_declare_an_invoice_already_paid()
    {
        // The request contract has no status field at all, so the only status a
        // generated invoice can have is the one the repository sets.
        Assert.Null(typeof(GenerateInvoiceRequest).GetProperty("Status"));

        var result = await StaffController(ManagerId)
            .Generate(ProjectId, new GenerateInvoiceRequest { Amount = 100m }, default);

        var body = Assert.IsType<InvoiceResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);

        Assert.Equal(InvoiceStatus.Pending, body.Status);
    }

    [Fact]
    public async Task The_invoice_is_attributed_to_the_caller_and_filed_under_the_route_project()
    {
        await StaffController(ManagerId)
            .Generate(ProjectId, new GenerateInvoiceRequest { Amount = 500m }, default);

        var call = Assert.Single(_invoices.Created);

        Assert.Equal(ManagerId, call.CreatedBy);
        Assert.Equal(ProjectId, call.ProjectId);
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_cannot_raise_an_invoice()
    {
        var result = await StaffController(callerId: null)
            .Generate(ProjectId, new GenerateInvoiceRequest { Amount = 100m }, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(_invoices.Created);
    }

    [Fact]
    public async Task An_invalid_amount_is_refused_before_anything_is_stored()
    {
        var controller = StaffController(ManagerId);
        controller.ModelState.AddModelError(
            nameof(GenerateInvoiceRequest.Amount), "The amount must be greater than zero.");

        var result = await controller.Generate(
            ProjectId, new GenerateInvoiceRequest { Amount = 0m }, default);

        var problem = Assert.IsType<ObjectResult>(result);
        var details = Assert.IsType<ValidationProblemDetails>(problem.Value);

        Assert.Contains(nameof(GenerateInvoiceRequest.Amount), details.Errors.Keys);
        Assert.Empty(_invoices.Created);
    }

    [Fact]
    public async Task Staff_see_a_projects_invoices_newest_first()
    {
        var controller = StaffController(ManagerId);
        await controller.Generate(ProjectId, new GenerateInvoiceRequest { Amount = 100m }, default);
        await controller.Generate(ProjectId, new GenerateInvoiceRequest { Amount = 200m }, default);

        var ok = Assert.IsType<OkObjectResult>(await controller.List(ProjectId, default));
        var body = Assert.IsAssignableFrom<IReadOnlyList<InvoiceResponse>>(ok.Value);

        Assert.Equal([200m, 100m], body.Select(invoice => invoice.Amount));
    }

    // ------------------------------------------- the Client's own view ----

    [Fact]
    public async Task The_owning_client_sees_the_invoices_raised_against_their_project()
    {
        // The "current cost" half of the story's purpose, alongside the
        // quotation's "expected cost".
        _owners.GiveOwnership(ProjectId, ClientId);
        await _invoices.CreateAsync(ProjectId, 300_000m, ManagerId);

        var ok = Assert.IsType<OkObjectResult>(await ClientController(ClientId).List(ProjectId, default));
        var body = Assert.IsAssignableFrom<IReadOnlyList<InvoiceResponse>>(ok.Value);

        Assert.Equal(300_000m, Assert.Single(body).Amount);
    }

    [Fact]
    public async Task A_client_who_does_not_own_the_project_is_refused_its_invoices()
    {
        _owners.GiveOwnership(ProjectId, SomeoneElse);
        await _invoices.CreateAsync(ProjectId, 300_000m, ManagerId);

        var problem = Assert.IsType<ObjectResult>(await ClientController(ClientId).List(ProjectId, default));
        var details = Assert.IsType<ProblemDetails>(problem.Value);

        Assert.Equal(StatusCodes.Status403Forbidden, details.Status);
    }

    [Fact]
    public async Task A_project_whose_owner_is_not_yet_known_is_refused_rather_than_allowed()
    {
        await _invoices.CreateAsync(ProjectId, 300_000m, ManagerId);

        var problem = Assert.IsType<ObjectResult>(await ClientController(ClientId).List(ProjectId, default));

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ProblemDetails>(problem.Value).Status);
    }

    [Fact]
    public async Task A_client_token_without_a_usable_subject_is_refused()
    {
        _owners.GiveOwnership(ProjectId, ClientId);

        Assert.IsType<UnauthorizedResult>(await ClientController(callerId: null).List(ProjectId, default));
    }

    private InvoicesController StaffController(Guid? callerId) =>
        new(_invoices) { ControllerContext = ContextFor(callerId, PlatformRoles.ProjectManager) };

    private ClientInvoicesController ClientController(Guid? callerId) =>
        new(_invoices, _owners) { ControllerContext = ContextFor(callerId, PlatformRoles.Client) };

    /// <summary>
    /// A signed-in caller in the given role. <paramref name="callerId"/> of
    /// <c>null</c> stands for a token that carried no usable <c>sub</c>.
    /// </summary>
    private static ControllerContext ContextFor(Guid? callerId, string role)
    {
        var claims = new List<Claim> { new("role", role) };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }
}
