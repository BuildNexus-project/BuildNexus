using System.Reflection;
using BuildNexus.PaymentService.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The US-03 rule applied to this service: every protected endpoint names the
/// roles allowed to call it.
/// </summary>
/// <remarks>
/// Written against the controllers by reflection rather than as a list of the
/// routes that exist today, so an endpoint added in a later story is held to the
/// same rule without anyone remembering to come back here. Needs no database.
/// </remarks>
public class EndpointRoleDeclarationTests
{
    [Fact]
    public void Every_endpoint_either_names_its_roles_or_is_explicitly_anonymous()
    {
        var undeclared = Endpoints()
            .Where(action => !IsAnonymous(action) && DeclaredRoles(action).Count == 0)
            .Select(Describe)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "Every protected endpoint must declare its roles with [Authorize(Roles = ...)], or be "
            + "explicitly [AllowAnonymous]. These declare neither: " + string.Join(", ", undeclared));
    }

    [Fact]
    public void No_endpoint_names_a_role_the_platform_does_not_have()
    {
        // A misspelled role is the quiet failure this guards against: it throws
        // nothing, it simply never matches, and the endpoint refuses everyone.
        var unknown = Endpoints()
            .SelectMany(action => DeclaredRoles(action).Select(role => (Action: action, Role: role)))
            .Where(pair => !PlatformRoles.All.Contains(pair.Role))
            .Select(pair => $"{Describe(pair.Action)} -> '{pair.Role}'")
            .ToList();

        Assert.True(
            unknown.Count == 0,
            "These endpoints name a role that is not a BuildNexus role: " + string.Join(", ", unknown));
    }

    [Fact]
    public void The_role_constants_are_spelled_the_way_a_token_carries_them()
    {
        // The wire contract with the User Service, which signs the role into the
        // token. A drift here refuses every caller silently.
        Assert.Equal(["Client", "Architect", "ProjectManager", "Admin"], PlatformRoles.All);
    }

    [Fact]
    public void Generating_and_reviewing_quotations_belongs_to_the_project_managers_and_admins()
    {
        // US-15 AC-1: the story names those two roles as the ones who generate a
        // cost estimate. An Architect designs and does not price, and a Client is
        // told the figure rather than setting it — their own view of these same
        // quotations is a separate read-only controller.
        Assert.Equal(
            [PlatformRoles.ProjectManager, PlatformRoles.Admin],
            RolesForActionOn<Controllers.QuotationsController>("Generate"));
        Assert.Equal(
            [PlatformRoles.ProjectManager, PlatformRoles.Admin],
            RolesForActionOn<Controllers.QuotationsController>("List"));
    }

    [Fact]
    public void Viewing_your_own_projects_quotations_is_the_clients_alone()
    {
        // US-15 AC-1: the estimate is viewable by the Client. The role is only
        // half the gate — the endpoint also refuses a Client who does not own the
        // project, which ClientQuotationsControllerTests pins. Staff roles are
        // outside it because they already have the richer view on
        // QuotationsController.
        Assert.Equal(
            [PlatformRoles.Client],
            RolesForActionOn<Controllers.ClientQuotationsController>("List"));
    }

    [Fact]
    public void Raising_and_reviewing_invoices_belongs_to_the_project_managers_and_admins()
    {
        // US-15 AC-2: billing is the same two roles' business as quoting. A
        // Client is billed rather than billing, and sees their own invoices
        // through ClientInvoicesController instead.
        Assert.Equal(
            [PlatformRoles.ProjectManager, PlatformRoles.Admin],
            RolesForActionOn<Controllers.InvoicesController>("Generate"));
        Assert.Equal(
            [PlatformRoles.ProjectManager, PlatformRoles.Admin],
            RolesForActionOn<Controllers.InvoicesController>("List"));
    }

    [Fact]
    public void Viewing_your_own_projects_invoices_is_the_clients_alone()
    {
        // The "current cost" half of the story's purpose. Ownership-scoped the
        // same way the quotation view is, which InvoicesControllerTests pins.
        Assert.Equal(
            [PlatformRoles.Client],
            RolesForActionOn<Controllers.ClientInvoicesController>("List"));
    }

    [Fact]
    public void Recording_a_payment_is_the_clients_alone()
    {
        // US-16: the story is framed around the Client paying down their own
        // balance. Staff raise the invoice (US-15) and do not pay it on the
        // customer's behalf. The role is only half the gate — the endpoint also
        // refuses a Client whose project the invoice is not on, which
        // ClientPaymentsControllerTests pins.
        Assert.Equal(
            [PlatformRoles.Client],
            RolesForActionOn<Controllers.ClientPaymentsController>("Record"));
    }

    private static IEnumerable<MethodInfo> Endpoints() =>
        typeof(PlatformRoles).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static IReadOnlyList<string> DeclaredRoles(MethodInfo action)
    {
        var roles = action.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>())
            .Select(attribute => attribute.Roles)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return roles is null ? [] : Split(roles);
    }

    private static bool IsAnonymous(MethodInfo action) =>
        action.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || action.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

    /// <summary>Roles declared on one action of one controller.</summary>
    private static IReadOnlyList<string> RolesForActionOn<TController>(string actionName)
        where TController : ControllerBase =>
        DeclaredRoles(typeof(TController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.Name == actionName
                              && method.GetCustomAttributes<HttpMethodAttribute>().Any()));

    private static IReadOnlyList<string> Split(string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Describe(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";
}
