using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Projects;

/// <summary>
/// <see cref="IProjectAccessClient"/> over HTTP. The <see cref="HttpClient"/> is
/// a typed client — its base address and timeout come from
/// <see cref="Configuration.ProjectServiceOptions"/> at registration.
/// </summary>
public class HttpProjectAccessClient : IProjectAccessClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpProjectAccessClient> _logger;

    public HttpProjectAccessClient(HttpClient httpClient, ILogger<HttpProjectAccessClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ProjectAccess> GetAccessAsync(
        Guid projectId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/projects/{projectId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable, refused, or slower than the client's timeout. Not the
            // caller's fault and not something they can fix — the upload answers
            // 502, it does not 403.
            _logger.LogError(
                ex, "Could not reach the Project Service to check access to project {ProjectId}.", projectId);

            return ProjectAccess.Unavailable;
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var project = await response.Content
                        .ReadFromJsonAsync<ProjectDetailPayload>(cancellationToken);

                    return ProjectAccess.Allowed(project?.AssignedArchitectId);

                case HttpStatusCode.NotFound:
                    return ProjectAccess.NotFound;

                case HttpStatusCode.Forbidden:
                    return ProjectAccess.Forbidden;

                case HttpStatusCode.Unauthorized:
                    // The gateway already validated the token to get here, and
                    // this service validated it again. If the Project Service
                    // still says 401 the two are misconfigured against each
                    // other — an operator problem, not the caller's.
                    _logger.LogError(
                        "The Project Service rejected a forwarded token as unauthenticated for project {ProjectId}; "
                        + "check the shared JWT settings match.", projectId);

                    return ProjectAccess.Unavailable;

                default:
                    _logger.LogError(
                        "The Project Service answered {StatusCode} checking access to project {ProjectId}.",
                        (int)response.StatusCode, projectId);

                    return ProjectAccess.Unavailable;
            }
        }
    }

    /// <summary>
    /// Just the one field of <c>ProjectDetailResponse</c> this service needs.
    /// Web defaults, so <c>assignedArchitectId</c> binds.
    /// </summary>
    private sealed record ProjectDetailPayload(Guid? AssignedArchitectId);
}
