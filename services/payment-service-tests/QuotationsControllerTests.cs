using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="QuotationsController"/> does with a request, over a fake
/// repository (US-15, AC-1).
/// </summary>
/// <remarks>
/// The role gate itself is not exercised here — that is the framework's job, and
/// <see cref="EndpointRoleDeclarationTests"/> pins which roles are declared.
/// These cover the decisions the controller makes once a caller is through it.
/// </remarks>
public class QuotationsControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly FakeQuotationRepository _quotations = new();

    [Fact]
    public async Task A_generated_quotation_is_linked_to_the_project_and_keeps_its_total()
    {
        // AC-1: linked to the project, stores an estimated total.
        var result = await ControllerFor(ManagerId)
            .Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 1_250_000.50m }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<QuotationResponse>(created.Value);

        Assert.Equal(ProjectId, body.ProjectId);
        Assert.Equal(1_250_000.50m, body.EstimatedTotal);
        Assert.NotEqual(Guid.Empty, body.Id);
    }

    [Fact]
    public async Task The_quotation_is_attributed_to_the_caller_not_to_anything_in_the_body()
    {
        // The author comes from the token's sub claim. The request contract has no
        // field for it, so a caller cannot credit an estimate to someone else.
        await ControllerFor(ManagerId)
            .Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 500_000m }, default);

        var call = Assert.Single(_quotations.Created);

        Assert.Equal(ManagerId, call.CreatedBy);
        Assert.Equal(ProjectId, call.ProjectId);
    }

    [Fact]
    public async Task The_project_comes_from_the_route()
    {
        // Two projects quoted by the same caller stay apart — the route id is what
        // the row is filed under.
        var other = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
        var controller = ControllerFor(ManagerId);

        await controller.Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 100m }, default);
        await controller.Generate(other, new GenerateQuotationRequest { EstimatedTotal = 200m }, default);

        Assert.Equal([ProjectId, other], _quotations.Created.Select(call => call.ProjectId));
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_cannot_generate_a_quotation()
    {
        // The role claim got the caller this far, but a quotation records who made
        // it — and nothing is written when that cannot be answered.
        var result = await ControllerFor(callerId: null)
            .Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 100m }, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(_quotations.Created);
    }

    [Fact]
    public async Task An_invalid_request_is_refused_before_anything_is_stored()
    {
        // ModelState stands in for the [Range] the binder would have failed on.
        var controller = ControllerFor(ManagerId);
        controller.ModelState.AddModelError(
            nameof(GenerateQuotationRequest.EstimatedTotal),
            "The estimated total must be greater than zero.");

        var result = await controller.Generate(
            ProjectId, new GenerateQuotationRequest { EstimatedTotal = 0m }, default);

        // ValidationProblem answers with RFC 7807 validation problem details
        // naming the offending field. The 400 itself is stamped on by MVC's
        // ProblemDetailsFactory, which a bare controller instance has no access
        // to, so it is not asserted here — the status lives on the endpoint's
        // [ProducesResponseType] and the refusal is what this test is about.
        var problem = Assert.IsType<ObjectResult>(result);
        var details = Assert.IsType<ValidationProblemDetails>(problem.Value);

        Assert.Contains(nameof(GenerateQuotationRequest.EstimatedTotal), details.Errors.Keys);
        Assert.Empty(_quotations.Created);
    }

    [Fact]
    public async Task Listing_returns_a_projects_quotations_newest_first()
    {
        // Re-quoting keeps both rows, and the current estimate is the one at the
        // front — the listing is what makes that visible.
        var controller = ControllerFor(ManagerId);
        await controller.Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 900_000m }, default);
        await controller.Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 1_100_000m }, default);

        var body = await ListFor(controller, ProjectId);

        Assert.Equal(2, body.Count);
        Assert.Equal(1_100_000m, body[0].EstimatedTotal);
        Assert.Equal(900_000m, body[1].EstimatedTotal);
    }

    [Fact]
    public async Task Listing_a_project_that_has_never_been_quoted_is_an_empty_list_not_a_failure()
    {
        // An unquoted project is a normal state. A 404 here would make the screen
        // treat "no estimate yet" as an error.
        Assert.Empty(await ListFor(ControllerFor(ManagerId), ProjectId));
    }

    [Fact]
    public async Task Listing_does_not_leak_another_projects_quotation()
    {
        var other = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000003");
        var controller = ControllerFor(ManagerId);
        await controller.Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 750_000m }, default);

        Assert.Empty(await ListFor(controller, other));
    }

    private async Task<IReadOnlyList<QuotationResponse>> ListFor(
        QuotationsController controller,
        Guid projectId)
    {
        var result = await controller.List(projectId, default);
        var ok = Assert.IsType<OkObjectResult>(result);

        return Assert.IsAssignableFrom<IReadOnlyList<QuotationResponse>>(ok.Value);
    }

    /// <summary>
    /// The controller with a signed-in Project Manager on it.
    /// <paramref name="callerId"/> of <c>null</c> stands for a token that carried
    /// no usable <c>sub</c>.
    /// </summary>
    private QuotationsController ControllerFor(Guid? callerId)
    {
        var claims = new List<Claim> { new(JwtOptionsRoleClaim, PlatformRoles.ProjectManager) };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new QuotationsController(_quotations)
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

    private const string JwtOptionsRoleClaim = "role";
}
