namespace BuildNexus.ConstructionService.Authorization;

/// <summary>
/// The four platform role names in the form
/// <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute.Roles"/>
/// needs them: compile-time constants, spelled exactly as the <c>role</c> claim
/// carries them.
/// </summary>
/// <remarks>
/// Declared again in this service rather than shared from another: the services
/// are independently deployable, and a compile-time dependency between them
/// would tie their release cycles together for four string constants. The names
/// are the wire contract — they must match the User Service exactly.
/// </remarks>
public static class PlatformRoles
{
    public const string Client = nameof(Client);

    public const string Architect = nameof(Architect);

    public const string ProjectManager = nameof(ProjectManager);

    public const string Admin = nameof(Admin);

    /// <summary>All four names, for code that has to enumerate the roles.</summary>
    /// <summary>
    /// The two roles that read the construction reports (US-19): an Admin overseeing
    /// the whole portfolio, and a Project Manager running the builds in it.
    /// </summary>
    /// <remarks>
    /// Spelled as one constant rather than repeated in an attribute, so the pair cannot
    /// drift between endpoints — and so widening it is one edit in one place.
    /// </remarks>
    public const string AdminOrProjectManager = $"{Admin},{ProjectManager}";

    public static readonly IReadOnlyList<string> All = [Client, Architect, ProjectManager, Admin];
}
