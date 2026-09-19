using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Users;

/// <summary>
/// Asks the User Service what role an account holds.
/// </summary>
/// <remarks>
/// The Project Service has no copy of the accounts and must not grow one — they
/// live in the User Service's own database. So before it fills the Architect or
/// Project Manager slot on a project it asks, over HTTP, carrying the Admin's
/// own bearer token: <c>GET /api/users/{id}</c> is Admin-only, and the Admin
/// making the assignment is exactly who is allowed to read it.
/// </remarks>
public interface IUserDirectoryClient
{
    /// <summary>
    /// Calls <c>GET /api/users/{userId}</c> on the User Service as the caller
    /// and maps the response.
    /// </summary>
    /// <param name="userId">The account to look up.</param>
    /// <param name="bearerToken">
    /// The caller's raw access token, without the <c>Bearer </c> prefix —
    /// forwarded so the User Service decides as if the caller asked it directly.
    /// </param>
    Task<UserLookup> GetUserAsync(Guid userId, string bearerToken, CancellationToken cancellationToken);
}
