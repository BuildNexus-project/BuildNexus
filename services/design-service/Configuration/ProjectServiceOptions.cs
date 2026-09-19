namespace BuildNexus.DesignService.Configuration;

/// <summary>
/// Where to reach the Project Service, bound from the
/// <c>Services:ProjectService</c> configuration section.
/// </summary>
/// <remarks>
/// This service does not own the fact of who is on a project — the Project
/// Service does. Before it accepts or serves a design document it asks that
/// service, forwarding the caller's own token, so the answer is scoped to
/// exactly what the caller may see there.
/// <para>
/// <c>http://project-service:8080/</c> on the compose network,
/// <c>http://localhost:5002/</c> for a native run. Validated at startup: a
/// service that cannot say where the Project Service is would refuse every
/// upload at runtime, and a log line after the first one is too late.
/// </para>
/// </remarks>
public class ProjectServiceOptions
{
    public const string SectionName = "Services:ProjectService";

    /// <summary>Absolute base URL, trailing slash included, e.g. <c>http://localhost:5002/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long a single access check may take before it is treated as unavailable.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
