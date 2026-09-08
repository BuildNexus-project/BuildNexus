namespace BuildNexus.ProjectService.Authorization;

/// <summary>
/// The four platform role names in the form <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute.Roles"/>
/// needs them: compile-time constants, spelled exactly as the <c>role</c> claim
/// carries them.
/// </summary>
/// <remarks>
/// A typo in a role name does not fail loudly — <c>[Authorize(Roles = "Projectmanager")]</c>
/// simply never matches, and the endpoint silently refuses everyone. Naming the
/// roles here once means the compiler catches what a raw string would not.
/// <para>
/// Declared again in this service rather than shared from the User Service: the
/// two are independently deployable, and a compile-time dependency between them
/// would tie their release cycles together for four string constants. The names
/// are the wire contract — they must match the User Service exactly, which is
/// what the role names in a token are.
/// </para>
/// </remarks>
public static class PlatformRoles
{
    public const string Client = nameof(Client);

    public const string Architect = nameof(Architect);

    public const string ProjectManager = nameof(ProjectManager);

    public const string Admin = nameof(Admin);

    /// <summary>
    /// Any signed-in user, whatever their role — for endpoints where the role
    /// is not what decides the answer.
    /// </summary>
    /// <remarks>
    /// Deliberately spelled out rather than written as a bare <c>[Authorize]</c>:
    /// every protected endpoint states the roles it accepts, and a token whose
    /// <c>role</c> claim is missing or unrecognised is refused rather than let
    /// through as "authenticated, role unknown".
    /// <para>
    /// Used by the endpoints that read a project. All four roles may reach
    /// them, but which projects each caller actually gets is a per-project
    /// question the role alone cannot answer — see
    /// <see cref="ProjectAccessPolicy"/>.
    /// </para>
    /// </remarks>
    public const string AnyRole = $"{Client},{Architect},{ProjectManager},{Admin}";

    /// <summary>
    /// The roles that move a project through its lifecycle: the two that staff
    /// and deliver it, plus Admin.
    /// </summary>
    /// <remarks>
    /// Client is outside it on purpose. A customer watches their project's
    /// progress; they do not declare the design approved or the build finished.
    /// </remarks>
    public const string ProjectStaffOrAdmin = $"{Architect},{ProjectManager},{Admin}";

    /// <summary>
    /// The two roles that may close a project out before it is built — the
    /// owning Client and an Admin (US-08).
    /// </summary>
    /// <remarks>
    /// The assigned Architect and Project Manager are outside it: abandoning a
    /// project before construction is the customer's or the company's call, not
    /// the people delivering it. Which of the two may cancel <em>this</em>
    /// project is <see cref="ProjectAccessPolicy.CanCancel"/>'s answer.
    /// </remarks>
    public const string ClientOrAdmin = $"{Client},{Admin}";

    /// <summary>All four names, for code that has to enumerate the roles.</summary>
    public static readonly IReadOnlyList<string> All = [Client, Architect, ProjectManager, Admin];
}
