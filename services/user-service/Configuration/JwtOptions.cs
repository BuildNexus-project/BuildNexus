namespace BuildNexus.UserService.Configuration;

/// <summary>
/// Signing and validation settings for the tokens this service issues,
/// bound from the <c>Jwt</c> configuration section.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Minimum key length for HMAC-SHA256, in bytes.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// HMAC signing key. Supply the real value out of band (environment variable
    /// <c>Jwt__SigningKey</c>) — never commit a production key.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long an issued token stays valid.</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
}
