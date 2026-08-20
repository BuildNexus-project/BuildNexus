using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Services;

/// <summary>A signed access token and the moment it stops being valid.</summary>
/// <param name="AccessToken">The signed JWT.</param>
/// <param name="ExpiresAtUtc">Expiry of the token, in UTC.</param>
public record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Issues signed JWTs for authenticated users.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a token carrying the user's id and role, signed with the
    /// configured key.
    /// </summary>
    IssuedToken CreateAccessToken(User user);
}
