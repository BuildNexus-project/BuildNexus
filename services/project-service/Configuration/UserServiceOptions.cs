namespace BuildNexus.ProjectService.Configuration;

/// <summary>
/// Where to reach the User Service, bound from the <c>Services:UserService</c>
/// configuration section.
/// </summary>
/// <remarks>
/// This service does not own what role an account holds — the User Service
/// does. Before it assigns an Architect or a Project Manager to a project it
/// asks that service, forwarding the Admin's own token, so a slot can only ever
/// be filled by someone who actually holds the matching role.
/// <para>
/// <c>http://user-service:8080/</c> on the compose network,
/// <c>http://localhost:5001/</c> for a native run. Validated at startup: a
/// service that cannot say where the User Service is would refuse every
/// assignment at runtime, and a log line after the first one is too late.
/// </para>
/// </remarks>
public class UserServiceOptions
{
    public const string SectionName = "Services:UserService";

    /// <summary>Absolute base URL, trailing slash included, e.g. <c>http://localhost:5001/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long a single role lookup may take before it is treated as unavailable.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
