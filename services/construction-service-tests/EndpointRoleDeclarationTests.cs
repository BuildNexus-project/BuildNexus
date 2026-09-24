using System.Reflection;
using BuildNexus.ConstructionService.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BuildNexus.ConstructionService.Tests;

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
    public void Listing_the_milestone_setups_is_the_admins_alone()
    {
        // US-23: the placeholders are an operational view of what the
        // DesignApproved consumer has done. An Admin checks a run without
        // needing a role on any of the projects involved; the clients and
        // architects on those projects do not see this list.
        Assert.Equal([PlatformRoles.Admin], RolesForActionOn<Controllers.MilestoneSetupsController>("List"));
    }

    [Fact]
    public void Managing_construction_milestones_is_the_project_managers_alone()
    {
        // US-12: the story is framed around the Project Manager — no other
        // role creates, moves, lists or reads these rows in this story.
        // US-13 gave the Client a read of the same data without widening this
        // gate: it is a separate Client-only endpoint on
        // ConstructionProgressController, so the PM's contract did not move and
        // the two gates cannot be widened by accident together.
        Assert.Equal([PlatformRoles.ProjectManager], RolesForActionOn<Controllers.MilestonesController>("Create"));
        Assert.Equal([PlatformRoles.ProjectManager], RolesForActionOn<Controllers.MilestonesController>("List"));
        Assert.Equal([PlatformRoles.ProjectManager], RolesForActionOn<Controllers.MilestonesController>("UpdateStatus"));
        Assert.Equal([PlatformRoles.ProjectManager], RolesForActionOn<Controllers.MilestonesController>("GetProgress"));
        Assert.Equal([PlatformRoles.ProjectManager], RolesForActionOn<Controllers.MilestonesController>("CreateFromTemplate"));
    }

    [Fact]
    public void Watching_a_projects_construction_progress_is_the_clients_alone()
    {
        // US-13: the story is framed around the Client watching their own
        // project. The role is only half the gate — the endpoint also refuses a
        // Client who does not own the project, which
        // ConstructionProgressControllerTests pins. Staff roles are outside it
        // because they already have the Project Manager's richer view on
        // MilestonesController, and an Admin administers accounts rather than
        // watching builds.
        Assert.Equal(
            [PlatformRoles.Client],
            RolesForActionOn<Controllers.ConstructionProgressController>("GetSummary"));
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

    /// <summary>
    /// Roles declared on one action of one controller. Disambiguates action
    /// names that appear on more than one controller — e.g. <c>List</c> lives
    /// on both <c>MilestoneSetupsController</c> and <c>MilestonesController</c>.
    /// </summary>
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