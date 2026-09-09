using System.Net;
using System.Net.Http.Json;
using BuildNexus.DesignService.Configuration;
using BuildNexus.DesignService.Models;
using Microsoft.Extensions.Options;

namespace BuildNexus.DesignService.Users;

/// <summary>
/// <see cref="IInternalUserClient"/> over HTTP. The <see cref="HttpClient"/> is
/// a typed client — its base address and timeout come from
/// <see cref="UserServiceOptions"/> at registration.
/// </summary>
public class HttpInternalUserClient : IInternalUserClient
{
    private const string ApiKeyHeader = "X-Internal-Api-Key";

    private readonly HttpClient _httpClient;
    private readonly InternalServiceOptions _internalServiceOptions;
    private readonly ILogger<HttpInternalUserClient> _logger;

    public HttpInternalUserClient(
        HttpClient httpClient,
        IOptions<InternalServiceOptions> internalServiceOptions,
        ILogger<HttpInternalUserClient> logger)
    {
        _httpClient = httpClient;
        _internalServiceOptions = internalServiceOptions.Value;
        _logger = logger;
    }

    public async Task<InternalUserLookup> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/users/{userId}");
        request.Headers.Add(ApiKeyHeader, _internalServiceOptions.ApiKey);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable, refused, or slower than the client's timeout. Not
            // something to fail the review over — see RevisionRequestNotifier.
            _logger.LogError(ex, "Could not reach the User Service to look up account {UserId}.", userId);

            return InternalUserLookup.Unavailable;
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var user = await response.Content.ReadFromJsonAsync<InternalUserPayload>(cancellationToken);

                    return user is { FullName: { Length: > 0 } fullName, Email: { Length: > 0 } email }
                        ? InternalUserLookup.Found(fullName, email)
                        : InternalUserLookup.Unavailable;

                case HttpStatusCode.NotFound:
                    return InternalUserLookup.NotFound;

                case HttpStatusCode.Unauthorized:
                    // The User Service refused the shared key outright — an
                    // operator problem (the two services disagree on
                    // InternalService__ApiKey), not anything about this
                    // particular lookup.
                    _logger.LogError(
                        "The User Service refused the internal API key looking up account {UserId}; check "
                        + "InternalService__ApiKey matches on both services.", userId);

                    return InternalUserLookup.Unavailable;

                default:
                    _logger.LogError(
                        "The User Service answered {StatusCode} looking up account {UserId}.",
                        (int)response.StatusCode, userId);

                    return InternalUserLookup.Unavailable;
            }
        }
    }

    /// <summary>
    /// Just the two fields of <c>InternalUserResponse</c> this service needs.
    /// Web defaults, so <c>fullName</c>/<c>email</c> bind.
    /// </summary>
    private sealed record InternalUserPayload(string? FullName, string? Email);
}
