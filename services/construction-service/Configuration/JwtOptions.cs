namespace BuildNexus.ConstructionService.Configuration;

/// <summary>
/// Validation settings for the tokens the User Service issues, bound from the
/// <c>Jwt</c> configuration section.
/// </summary>
/// <remarks>
/// This service only ever validates tokens — it never signs one — so it takes
/// the three settings of the shared convention and nothing else. All three
/// values must be identical to the ones the User Service signs with, or every
/// token it issues is refused here.
/// </remarks>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Minimum key length for HMAC-SHA256, in bytes.</summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// The claim the role is carried in — the short <c>role</c>, not the long
    /// WS-Federation URI. Must be set on
    /// <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters.RoleClaimType"/>
    /// explicitly, or every <c>[Authorize(Roles = ...)]</c> silently refuses
    /// everyone.
    /// </summary>
    public const string RoleClaimType = "role";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// HMAC signing key. Supply the real value out of band (environment variable
    /// <c>Jwt__SigningKey</c>) — never commit a production key.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;
}
