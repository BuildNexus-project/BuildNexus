using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Authorization;

/// <summary>
/// The four platform role names in the form <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute.Roles"/>
/// needs them: compile-time constants, spelled exactly as <see cref="UserRole"/>
/// spells them and exactly as the <c>role</c> claim carries them.
/// </summary>
/// <remarks>
/// A typo in a role name does not fail loudly — <c>[Authorize(Roles = "Projectmanager")]</c>
/// simply never matches, and the endpoint silently refuses everyone. Naming the
/// roles here once, off the enum, means the compiler catches what a raw string
/// would not.
/// </remarks>
public static class PlatformRoles
{
    public const string Client = nameof(UserRole.Client);

    public const string Architect = nameof(UserRole.Architect);

    public const string ProjectManager = nameof(UserRole.ProjectManager);

    public const string Admin = nameof(UserRole.Admin);

    /// <summary>
    /// Any signed-in user, whatever their role — for endpoints that act on the
    /// caller's own account.
    /// </summary>
    /// <remarks>
    /// Deliberately spelled out rather than written as a bare <c>[Authorize]</c>:
    /// every protected endpoint states the roles it accepts, and a token whose
    /// <c>role</c> claim is missing or unrecognised is refused rather than let
    /// through as "authenticated, role unknown".
    /// </remarks>
    public const string AnyRole = $"{Client},{Architect},{ProjectManager},{Admin}";

    /// <summary>
    /// The two roles that staff and deliver a project. Client is outside it —
    /// a customer has no business browsing the firm's staff — and so is Admin,
    /// who administers accounts rather than working on projects.
    /// </summary>
    public const string ProjectStaff = $"{Architect},{ProjectManager}";

    /// <summary>All four names, for code that has to enumerate the roles.</summary>
    public static readonly IReadOnlyList<string> All = [Client, Architect, ProjectManager, Admin];
}
