namespace BuildNexus.DesignService.Configuration;

/// <summary>
/// Where to reach the User Service, bound from the <c>Services:UserService</c>
/// configuration section.
/// </summary>
/// <remarks>
/// Used only for <c>/api/internal</c> lookups — see
/// <see cref="Users.HttpInternalUserClient"/> — so a revision request can name
/// the Architect to notify. Validated at startup for the same reason
/// <see cref="ProjectServiceOptions"/> is.
/// </remarks>
public class UserServiceOptions
{
    public const string SectionName = "Services:UserService";

    /// <summary>Absolute base URL, trailing slash included, e.g. <c>http://localhost:5001/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long a single lookup may take before it is treated as unavailable.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
