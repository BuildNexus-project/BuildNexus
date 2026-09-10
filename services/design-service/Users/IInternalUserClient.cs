using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Users;

/// <summary>
/// Looks up a BuildNexus account's name and email, to notify them.
/// </summary>
/// <remarks>
/// This service has no copy of the accounts and must not grow one — they live
/// in the User Service's own database. So before it names the Architect on a
/// revision request it asks, over HTTP, presenting the shared internal key
/// rather than the Client's own token: naming who uploaded a version is not
/// something a Client's own access should be asked to cover, and
/// <c>GET /api/users/{id}</c> is Admin-only in any case.
/// </remarks>
public interface IInternalUserClient
{
    /// <summary>Calls <c>GET /api/internal/users/{userId}</c> on the User Service and maps the response.</summary>
    Task<InternalUserLookup> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
