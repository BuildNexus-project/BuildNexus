using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// Mints the tokens these tests present to the gateway, in the same shape the
/// User Service issues them: HMAC-SHA256, with the user id in <c>sub</c> and
/// the platform role in <c>role</c>.
/// </summary>
/// <remarks>
/// The gateway never signs a token, so a test cannot ask it for one, and
/// reaching for the real User Service would drag a database into a suite that
/// otherwise needs nothing. Minting here also allows the cases a healthy
/// service will not produce — an expired token, a foreign issuer, one signed
/// with the wrong key — which are precisely the ones the gateway exists to
/// refuse.
/// </remarks>
public static class TestTokens
{
    public static string Valid(string role = "Admin") => Create(role: role);

    /// <summary>Issued two hours ago and good for one, so it expired an hour ago.</summary>
    public static string Expired() =>
        Create(issuedAt: DateTime.UtcNow.AddHours(-2), lifetime: TimeSpan.FromHours(1));

    /// <summary>Correctly formed and correctly claimed, but signed by someone else.</summary>
    public static string SignedWithAnotherKey() =>
        Create(signingKey: "a-different-key-of-quite-sufficient-length-for-hmac-sha256");

    public static string WithIssuer(string issuer) => Create(issuer: issuer);

    public static string WithAudience(string audience) => Create(audience: audience);

    /// <summary>A valid token whose signature no longer matches its payload.</summary>
    public static string Tampered()
    {
        var token = Valid();
        var lastCharacter = token[^1];
        return token[..^1] + (lastCharacter == 'A' ? 'B' : 'A');
    }

    private static string Create(
        string role = "Admin",
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        DateTime? issuedAt = null,
        TimeSpan? lifetime = null)
    {
        // The whole window moves together, so an expired token is one whose
        // lifetime has genuinely passed rather than one whose exp precedes its
        // own nbf -- which is a malformed token, not an expired one.
        var start = issuedAt ?? DateTime.UtcNow;
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(signingKey ?? GatewayFactory.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? GatewayFactory.Issuer,
            Audience = audience ?? GatewayFactory.Audience,
            IssuedAt = start,
            NotBefore = start.AddSeconds(-5),
            Expires = start.Add(lifetime ?? TimeSpan.FromMinutes(60)),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Email] = "someone@buildnexus.local",
                [JwtRegisteredClaimNames.Name] = "Someone",
                ["role"] = role
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
