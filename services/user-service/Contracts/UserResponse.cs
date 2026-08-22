namespace BuildNexus.UserService.Contracts;

/// <summary>
/// A user as returned to callers. Carries no password material.
/// </summary>
public class UserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}
