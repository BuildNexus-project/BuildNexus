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

    /// <summary>All four names, for code that has to enumerate the roles.</summary>
    public static readonly IReadOnlyList<string> All = [Client, Architect, ProjectManager, Admin];
}
