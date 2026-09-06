using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Users;

/// <summary>
/// <see cref="IUserDirectoryClient"/> over HTTP. The <see cref="HttpClient"/> is
/// a typed client — its base address and timeout come from
/// <see cref="Configuration.UserServiceOptions"/> at registration.
/// </summary>
public class HttpUserDirectoryClient : IUserDirectoryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpUserDirectoryClient> _logger;

    public HttpUserDirectoryClient(HttpClient httpClient, ILogger<HttpUserDirectoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UserLookup> GetUserAsync(
        Guid userId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable, refused, or slower than the client's timeout. Not the
            // Admin's fault and not something they can fix — the assignment
            // answers 502, it does not pretend the role check passed.
            _logger.LogError(
                ex, "Could not reach the User Service to look up account {UserId}.", userId);

            return UserLookup.Unavailable;
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var user = await response.Content.ReadFromJsonAsync<UserPayload>(cancellationToken);

                    return user?.Role is { Length: > 0 } role
                        ? UserLookup.Found(role)
                        : UserLookup.Unavailable;

                case HttpStatusCode.NotFound:
                    return UserLookup.NotFound;

                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    // The gateway validated the token to get here and this
                    // service validated it again, so a 401/403 from the User
                    // Service means the two are misconfigured against each other
                    // — an operator problem, not the Admin's.
                    _logger.LogError(
                        "The User Service refused a forwarded Admin token ({StatusCode}) looking up account "
                        + "{UserId}; check the shared JWT settings match.",
                        (int)response.StatusCode, userId);

                    return UserLookup.Unavailable;

                default:
                    _logger.LogError(
                        "The User Service answered {StatusCode} looking up account {UserId}.",
                        (int)response.StatusCode, userId);

                    return UserLookup.Unavailable;
            }
        }
    }

    /// <summary>
    /// Just the one field of <c>UserResponse</c> this service needs. Web
    /// defaults, so <c>role</c> binds.
    /// </summary>
    private sealed record UserPayload(string? Role);
}
