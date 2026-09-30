using System.Security.Claims;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The Client's own view of their project's quotations (US-15, AC-1 —
/// "viewable by the Client").
/// </summary>
/// <remarks>
/// The role gate is only half of it: these cover the other half, that a Client
/// who does not own the project is refused whatever the project's real state is.
/// </remarks>
public class ClientQuotationsControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private readonly FakeQuotationRepository _quotations = new();
    private readonly FakeProjectOwnerRepository _owners = new();

    [Fact]
    public async Task The_owning_client_sees_their_projects_quotations_newest_first()
    {
        // AC-1: the estimate is viewable by the Client, and the first entry is
        // what the project is currently expected to cost.
        _owners.GiveOwnership(ProjectId, ClientId);
        await _quotations.CreateAsync(ProjectId, 900_000m, Guid.NewGuid());
        await _quotations.CreateAsync(ProjectId, 1_100_000m, Guid.NewGuid());

        var body = await ListFor(ClientId);

        Assert.Equal(2, body.Count);
        Assert.Equal(1_100_000m, body[0].EstimatedTotal);
        Assert.Equal(900_000m, body[1].EstimatedTotal);
    }

    [Fact]
    public async Task A_client_who_does_not_own_the_project_is_refused()
    {
        // Without this, any signed-in Client could read any project's cost
        // estimates by guessing an id — the role gate alone does not scope it.
        _owners.GiveOwnership(ProjectId, SomeoneElse);
        await _quotations.CreateAsync(ProjectId, 750_000m, Guid.NewGuid());

        var result = await ControllerFor(ClientId).List(ProjectId, default);

        AssertForbidden(result);
    }

    [Fact]
    public async Task A_project_whose_owner_is_not_yet_known_is_refused_rather_than_allowed()
    {
        // The ProjectCreated event may not have been consumed yet. Denying is the
        // safe direction: defaulting an unknown project to "allowed" would hand
        // every Client every project whose ownership happens to be missing.
        await _quotations.CreateAsync(ProjectId, 500_000m, Guid.NewGuid());

        AssertForbidden(await ControllerFor(ClientId).List(ProjectId, default));
    }

    [Fact]
    public async Task A_project_that_is_not_theirs_looks_the_same_whether_or_not_it_has_quotations()
    {
        // Ownership is checked before anything is read, so a Client probing ids
        // cannot tell a quoted project from an unquoted one from one that does
        // not exist at all.
        var quoted = ProjectId;
        var unquoted = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000009");
        _owners.GiveOwnership(quoted, SomeoneElse);
        _owners.GiveOwnership(unquoted, SomeoneElse);
        await _quotations.CreateAsync(quoted, 750_000m, Guid.NewGuid());

        var onQuoted = await ControllerFor(ClientId).List(quoted, default);
        var onUnquoted = await ControllerFor(ClientId).List(unquoted, default);

        Assert.Equal(Status(onQuoted), Status(onUnquoted));
        Assert.Equal(Title(onQuoted), Title(onUnquoted));
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_is_refused()
    {
        // The role claim got the caller this far, but without a subject there is
        // no "their own project" to scope the read to.
        _owners.GiveOwnership(ProjectId, ClientId);

        Assert.IsType<UnauthorizedResult>(await ControllerFor(callerId: null).List(ProjectId, default));
    }

    [Fact]
    public async Task An_owned_project_with_no_quotation_yet_is_an_empty_list_not_a_refusal()
    {
        // "No estimate yet" is a normal state the screen should be able to show.
        _owners.GiveOwnership(ProjectId, ClientId);

        Assert.Empty(await ListFor(ClientId));
    }

    private async Task<IReadOnlyList<QuotationResponse>> ListFor(Guid clientId)
    {
        var result = await ControllerFor(clientId).List(ProjectId, default);
        var ok = Assert.IsType<OkObjectResult>(result);

        return Assert.IsAssignableFrom<IReadOnlyList<QuotationResponse>>(ok.Value);
    }

    private static void AssertForbidden(IActionResult result)
    {
        var problem = Assert.IsType<ObjectResult>(result);
        var details = Assert.IsType<ProblemDetails>(problem.Value);

        Assert.Equal(StatusCodes.Status403Forbidden, details.Status);
    }

    private static int? Status(IActionResult result) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Status;

    private static string? Title(IActionResult result) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value).Title;

    /// <summary>
    /// The controller with a signed-in Client on it. <paramref name="callerId"/>
    /// of <c>null</c> stands for a token that carried no usable <c>sub</c>.
    /// </summary>
    private ClientQuotationsController ControllerFor(Guid? callerId)
    {
        var claims = new List<Claim> { new("role", PlatformRoles.Client) };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ClientQuotationsController(_quotations, _owners)
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
