using System.Reflection;
using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The first half of US-03 as a test: every protected endpoint names the roles
/// allowed to call it.
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
    public void The_role_constants_match_the_roles_the_platform_actually_has()
    {
        // PlatformRoles is what the endpoints authorise against and UserRole is
        // what the database stores; if they ever drift apart, an endpoint would
        // be guarding a role nobody can hold.
        Assert.Equal(Enum.GetNames<UserRole>(), PlatformRoles.All);
    }

    [Theory]
    [InlineData(PlatformRoles.AnyRole, 4)]
    [InlineData(PlatformRoles.ProjectStaff, 2)]
    public void A_role_grouping_is_made_of_real_roles(string grouping, int expectedCount)
    {
        var roles = Split(grouping);

        Assert.Equal(expectedCount, roles.Count);
        Assert.All(roles, role => Assert.Contains(role, PlatformRoles.All));
    }

    [Fact]
    public void The_users_endpoints_are_gated_exactly_as_the_story_says()
    {
        // The matrix US-03 asks for, pinned so a later change to any of these
        // has to be a deliberate one.
        Assert.Equal(PlatformRoles.All, RolesFor("GetCurrentUser"));
        Assert.Equal(PlatformRoles.All, RolesFor("UpdateCurrentUser"));
        Assert.Equal([PlatformRoles.Architect, PlatformRoles.ProjectManager], RolesFor("GetProjectStaffDirectory"));
        Assert.Equal([PlatformRoles.Admin], RolesFor("GetAll"));
        Assert.Equal([PlatformRoles.Admin], RolesFor("GetById"));
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
