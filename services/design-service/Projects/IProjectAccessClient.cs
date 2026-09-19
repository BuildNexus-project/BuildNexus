using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Projects;

/// <summary>
/// Asks the Project Service whether the current caller may touch a project.
/// </summary>
/// <remarks>
/// The Design Service has no copy of who is on a project and must not grow one
/// — that data lives in the Project Service's own database. So it asks, over
/// HTTP, carrying the caller's own bearer token: the Project Service applies
/// its own per-project rule (owning client, assigned staff, or Admin) and this
/// service relays the outcome.
/// </remarks>
public interface IProjectAccessClient
{
    /// <summary>
    /// Calls <c>GET /api/projects/{projectId}</c> on the Project Service as the
    /// caller and maps the response.
    /// </summary>
    /// <param name="projectId">The project to check.</param>
    /// <param name="bearerToken">
    /// The caller's raw access token, without the <c>Bearer </c> prefix —
    /// forwarded so the Project Service decides as if the caller asked it
    /// directly.
    /// </param>
    Task<ProjectAccess> GetAccessAsync(Guid projectId, string bearerToken, CancellationToken cancellationToken);
}
