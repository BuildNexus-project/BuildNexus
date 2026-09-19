namespace BuildNexus.UserService.Configuration;

/// <summary>
/// The shared secret other BuildNexus services present to call this service's
/// internal, non-user-facing endpoints — see <see cref="Authorization.InternalServiceAuthenticationHandler"/>.
/// </summary>
public class InternalServiceOptions
{
    public const string SectionName = "InternalService";

    /// <summary>Minimum key length, the same bar as a JWT signing key.</summary>
    public const int MinimumApiKeyBytes = 32;

    /// <summary>
    /// Presented by a caller in the <c>X-Internal-Api-Key</c> header. Supply the
    /// real value out of band (environment variable <c>InternalService__ApiKey</c>)
    /// — never commit a production key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
