using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Data;

/// <summary>
/// Data access for the <c>users</c> table.
/// </summary>
public interface IUserRepository
{
    /// <summary>Returns true when the email is already registered (case-insensitive).</summary>
    Task<bool> EmailExistsAsync(string email);

    /// <summary>Returns the user with this email, or <c>null</c> when there is none.</summary>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>Returns the user with this id, or <c>null</c> when there is none.</summary>
    Task<User?> GetByIdAsync(Guid id);

    /// <summary>Returns true when at least one Admin account exists.</summary>
    Task<bool> AdminExistsAsync();

    /// <summary>Inserts a new user row.</summary>
    Task InsertAsync(User user);

    /// <summary>
    /// Writes the self-editable profile fields — full name and contact details —
    /// for an existing user. Email, role and password hash are never touched.
    /// </summary>
    /// <returns><c>false</c> when no row with this id exists.</returns>
    Task<bool> UpdateProfileAsync(User user);
}
