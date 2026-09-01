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

    /// <summary>
    /// Returns one page of accounts, active or not, ordered by name — the Admin
    /// directory, which is the one view that must show deactivated accounts too.
    /// </summary>
    /// <param name="role">Narrows the page to a single role; <c>null</c> lists every role.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <remarks>
    /// The total counts the accounts matching <paramref name="role"/>, not the
    /// table, so the caller can tell a last page from a full one. The password
    /// hash is not read: a listing has no use for it.
    /// </remarks>
    Task<PagedResult<User>> ListPageAsync(UserRole? role, int page, int pageSize);

    /// <summary>
    /// Returns the active accounts holding any of these roles, ordered by name.
    /// </summary>
    /// <remarks>
    /// Deactivated accounts are left out: this backs the project-staff lookup,
    /// and someone who can no longer sign in cannot be given work. The password
    /// hash is not read.
    /// </remarks>
    Task<IReadOnlyList<User>> ListActiveByRolesAsync(IReadOnlyCollection<UserRole> roles);

    /// <summary>Inserts a new user row.</summary>
    Task InsertAsync(User user);

    /// <summary>
    /// Writes the self-editable profile fields — full name and contact details —
    /// for an existing user. Email, role and password hash are never touched.
    /// </summary>
    /// <returns><c>false</c> when no row with this id exists.</returns>
    Task<bool> UpdateProfileAsync(User user);

    /// <summary>
    /// Writes the fields only an administrator may change — full name, email and
    /// role. The password hash and the active flag are never touched.
    /// </summary>
    /// <returns><c>false</c> when no row with this id exists.</returns>
    /// <exception cref="DuplicateEmailException">
    /// The new email already belongs to another account.
    /// </exception>
    Task<bool> UpdateAccountAsync(User user);

    /// <summary>
    /// Turns an account's access on or off. Deactivating is what stops the
    /// holder signing in; nothing else about the account changes.
    /// </summary>
    /// <returns><c>false</c> when no row with this id exists.</returns>
    Task<bool> SetActiveAsync(Guid userId, bool isActive, DateTime updatedAtUtc);

    /// <summary>
    /// Replaces a user's stored password hash, which is what retires the old
    /// password: it is overwritten, so nothing can verify against it again.
    /// </summary>
    /// <returns><c>false</c> when no row with this id exists.</returns>
    Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, DateTime updatedAtUtc);
}
