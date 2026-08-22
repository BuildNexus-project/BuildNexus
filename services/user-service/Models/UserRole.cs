namespace BuildNexus.UserService.Models;

/// <summary>
/// The four BuildNexus platform roles. Persisted as its string name in
/// <c>users.role</c> and carried in the <c>role</c> claim of an issued JWT.
/// </summary>
public enum UserRole
{
    Client,
    Architect,
    ProjectManager,
    Admin
}
