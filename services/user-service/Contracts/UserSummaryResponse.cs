namespace BuildNexus.UserService.Contracts;

/// <summary>
/// One row of the Admin user directory. Carries no password material and no
/// contact details — the directory is a roster, and a single account's details
/// are read through <c>GET /api/users/{id}</c>.
/// </summary>
public class UserSummaryResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// <c>false</c> for a deactivated account. Deliberately part of this
    /// response: the directory is the one view that has to show accounts that
    /// can no longer sign in.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
