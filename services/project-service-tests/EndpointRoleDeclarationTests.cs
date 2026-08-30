using System.Reflection;
using BuildNexus.ProjectService.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BuildNexus.ProjectService.Tests;

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
        // These are the wire contract with the User Service, which signs the
        // role into the token. A drift here refuses every caller silently.
        Assert.Equal(["Client", "Architect", "ProjectManager", "Admin"], PlatformRoles.All);
    }

    [Fact]
    public void Submitting_a_project_is_gated_exactly_as_the_story_says()
    {
        // US-05 is a Client describing the building they want. Staff roles do
        // not submit work on a customer's behalf, so this is pinned rather than
        // left to whoever edits the controller next.
        Assert.Equal([PlatformRoles.Client], RolesFor("CreateProject"));
    }

    /// <summary>Every controller action in the service, found by its HTTP verb attribute.</summary>
    private static IEnumerable<MethodInfo> Endpoints() =>
        typeof(PlatformRoles).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

    /// <summary>
    /// The roles an action accepts, taken from the action itself or, failing
    /// that, from its controller — which is how ASP.NET Core reads them too.
    /// </summary>
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

    private static IReadOnlyList<string> RolesFor(string actionName) =>
        DeclaredRoles(Endpoints().Single(action => action.Name == actionName));

    private static IReadOnlyList<string> Split(string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Describe(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";
}
