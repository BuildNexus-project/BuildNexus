namespace BuildNexus.UserService.Contracts;

/// <summary>
/// A user as returned to callers. Carries no password material.
/// </summary>
public class UserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary><c>null</c> when the user has not given a contact number.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary><c>null</c> when the user has not given a contact address.</summary>
    public string? ContactAddress { get; set; }

    public string Role { get; set; } = string.Empty;
}
