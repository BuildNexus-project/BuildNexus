namespace BuildNexus.ConstructionService.Configuration;

/// <summary>
/// Where to reach the Project Service, bound from the <c>Services:ProjectService</c>
/// configuration section.
/// </summary>
/// <remarks>
/// Used for one question: which projects may the current Project Manager see. This service
/// records who owns a project but not which Project Manager runs it — that fact lives in the
/// Project Service's own database — so it asks, carrying the caller's own token, rather than
/// growing a copy that would go stale the moment a project is reassigned.
/// </remarks>
public class ProjectServiceOptions
{
    public const string SectionName = "Services:ProjectService";

    /// <summary>The Project Service's address: <c>http://project-service:8080/</c> in the stack, <c>http://localhost:5002/</c> for a native run.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for it before treating it as unreachable.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
