namespace BuildNexus.DesignService.Configuration;

/// <summary>
/// The shared secret this service presents when calling another service's
/// internal endpoints, bound from the <c>InternalService</c> configuration
/// section.
/// </summary>
/// <remarks>
/// The sending side of what <c>user-service</c>'s identically-named
/// <c>InternalServiceOptions</c> validates — the same value,
/// <c>InternalService__ApiKey</c>, configured identically on both services, the
/// same convention the JWT signing key already follows.
/// </remarks>
public class InternalServiceOptions
{
    public const string SectionName = "InternalService";

    /// <summary>Minimum key length, the same bar as a JWT signing key.</summary>
    public const int MinimumApiKeyBytes = 32;

    /// <summary>
    /// Sent as the <c>X-Internal-Api-Key</c> header. Supply the real value out
    /// of band (environment variable <c>InternalService__ApiKey</c>) — never
    /// commit a production key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
