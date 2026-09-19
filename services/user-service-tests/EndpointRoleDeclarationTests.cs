using System.Reflection;
using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Controllers;
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
            .Where(action => !IsAnonymous(action)
                && !UsesADifferentAuthenticationScheme(action)
                && DeclaredRoles(action).Count == 0)
            .Select(Describe)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "Every protected endpoint must declare its roles with [Authorize(Roles = ...)], be "
            + "explicitly [AllowAnonymous], or name a non-default AuthenticationSchemes (a caller other "
            + "than a signed-in user, gated some other way). These declare none of those: "
            + string.Join(", ", undeclared));
    }

    [Fact]
    public void The_internal_user_lookup_is_gated_by_the_internal_scheme_not_a_role()
    {
        // A caller here holds the shared internal key, not a BuildNexus account
        // — RolesFor's usual question ("which role") does not apply, and this
        // pins that it is covered some other way rather than by accident.
        var action = Endpoints().Single(a => a.DeclaringType == typeof(InternalController) && a.Name == "GetById");

        Assert.False(IsAnonymous(action));
        Assert.Empty(DeclaredRoles(action));
        Assert.True(UsesADifferentAuthenticationScheme(action));
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

        // GetById also exists on InternalController (a different lookup, gated a
        // different way — see the test above) — RolesFor is scoped to
        // UsersController so the two are never confused for each other.

        // US-37: administering an account is Admin only, on the way in as well
        // as on the way out. Editing someone's role is the authorisation
        // boundary itself, so nothing below Admin may reach either of these.
        Assert.Equal([PlatformRoles.Admin], RolesFor("UpdateUser"));
        Assert.Equal([PlatformRoles.Admin], RolesFor("SetUserActive"));
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

    /// <summary>
    /// True for an endpoint gated by something other than the default JWT
    /// bearer scheme — a caller proving it is a trusted service, not a
    /// signed-in user, so "which role" is not the applicable question.
    /// </summary>
    private static bool UsesADifferentAuthenticationScheme(MethodInfo action) =>
        action.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>())
            .Select(attribute => attribute.AuthenticationSchemes)
            .Any(schemes => !string.IsNullOrWhiteSpace(schemes));

    /// <summary>Scoped to <see cref="UsersController"/> — other controllers can and do reuse action names.</summary>
    private static IReadOnlyList<string> RolesFor(string actionName) =>
        DeclaredRoles(Endpoints()
            .Where(action => action.DeclaringType == typeof(UsersController))
            .Single(action => action.Name == actionName));

    private static IReadOnlyList<string> Split(string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Describe(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";
}
