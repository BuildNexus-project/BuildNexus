using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Projects;

/// <summary>
/// <see cref="IProjectDirectoryClient"/> over HTTP. The <see cref="HttpClient"/> is a typed
/// client — its base address and timeout come from
/// <see cref="Configuration.ProjectServiceOptions"/> at registration.
/// </summary>
public class HttpProjectDirectoryClient : IProjectDirectoryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpProjectDirectoryClient> _logger;

    public HttpProjectDirectoryClient(HttpClient httpClient, ILogger<HttpProjectDirectoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<VisibleProjects> ListVisibleProjectsAsync(
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/projects");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable, refused, or slower than the client's timeout. Not the caller's
            // fault and not something they can fix — the dashboard answers 502, it does
            // not pretend they have no projects.
            _logger.LogError(ex, "Could not reach the Project Service to list the caller's projects.");

            return VisibleProjects.Unavailable;
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                // A 401 here means the two services disagree about the signing key — an
                // operator problem, not the caller's. Anything else is the Project Service
                // having a bad moment. Either way there is no list to relay.
                _logger.LogError(
                    "The Project Service answered {StatusCode} listing the caller's projects.",
                    (int)response.StatusCode);

                return VisibleProjects.Unavailable;
            }

            var projects = await response.Content
                .ReadFromJsonAsync<List<ProjectSummaryPayload>>(cancellationToken) ?? [];

            return VisibleProjects.Available(
                projects.Select(project => new VisibleProject(project.Id, project.Name)).ToList());
        }
    }

    /// <summary>
    /// Just the two fields of <c>ProjectSummaryResponse</c> this service needs. Web defaults,
    /// so <c>id</c> and <c>name</c> bind.
    /// </summary>
    private sealed record ProjectSummaryPayload(Guid Id, string Name);
}
