using System.Text;
using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BuildNexus.UserService.Services;

/// <summary>
/// Issues HMAC-SHA256 signed JWTs. The token carries the user id in <c>sub</c>
/// and the platform role in <c>role</c>, which is what the API Gateway and the
/// downstream services authorise against.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    /// <summary>
    /// Claim type carrying the platform role. Deliberately the short name rather
    /// than the WS-Federation URI, so services on other stacks can read it too.
    /// </summary>
    public const string RoleClaimType = "role";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public IssuedToken CreateAccessToken(User user)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Name] = user.FullName,
                [RoleClaimType] = user.Role.ToString()
            }
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new IssuedToken(token, expiresAt);
    }
}
