using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Projects;

/// <summary>
/// Asks the Project Service which projects the current caller may see.
/// </summary>
/// <remarks>
/// The list-shaped sibling of <see cref="IProjectAccessClient"/>, for the same
/// reason: this service has no copy of who owns or is assigned to a project and
/// must not grow one. A dashboard needs "every project this person is on", and
/// only the Project Service can say — so it is asked, over HTTP, carrying the
/// caller's own bearer token, and it applies its own rule (a Client's own
/// projects, an Architect's assigned ones).
/// </remarks>
public interface IProjectDirectoryClient
{
    /// <summary>
    /// Calls <c>GET /api/projects</c> on the Project Service as the caller and
    /// maps the response.
    /// </summary>
    /// <param name="bearerToken">
    /// The caller's raw access token, without the <c>Bearer </c> prefix —
    /// forwarded so the Project Service decides as if the caller asked it
    /// directly.
    /// </param>
    Task<VisibleProjects> ListVisibleProjectsAsync(string bearerToken, CancellationToken cancellationToken);
}
