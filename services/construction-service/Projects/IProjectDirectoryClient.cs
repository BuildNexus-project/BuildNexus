using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Projects;

/// <summary>
/// Asks the Project Service which projects the current caller may see.
/// </summary>
/// <remarks>
/// This service records who owns a project (from <c>ProjectCreated</c>) but not which Project
/// Manager runs it, and must not grow a copy: that fact lives in the Project Service's own
/// database and changes whenever an Admin reassigns a project. So it is asked, over HTTP,
/// carrying the caller's own bearer token, and the Project Service applies its own rule — for
/// a Project Manager, the projects they are assigned to.
/// </remarks>
public interface IProjectDirectoryClient
{
    /// <summary>
    /// Calls <c>GET /api/projects</c> on the Project Service as the caller and maps the response.
    /// </summary>
    /// <param name="bearerToken">
    /// The caller's raw access token, without the <c>Bearer </c> prefix — forwarded so the
    /// Project Service decides as if the caller asked it directly.
    /// </param>
    Task<VisibleProjects> ListVisibleProjectsAsync(string bearerToken, CancellationToken cancellationToken);
}
