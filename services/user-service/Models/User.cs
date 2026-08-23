namespace BuildNexus.UserService.Models;

/// <summary>
/// A registered BuildNexus user, mapped by hand from the <c>users</c> table.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Optional contact number; <c>null</c> until the user fills it in.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Optional postal or site address; <c>null</c> until the user fills it in.</summary>
    public string? ContactAddress { get; set; }

    /// <summary>Never the raw password — always the hash produced by the password hasher.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
