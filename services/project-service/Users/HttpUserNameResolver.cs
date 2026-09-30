using System.Net;
using System.Net.Http.Json;
using BuildNexus.ProjectService.Configuration;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Users;

/// <summary>
/// <see cref="IUserNameResolver"/> over HTTP. The <see cref="HttpClient"/> is a
/// typed client — its base address and timeout come from
/// <see cref="UserServiceOptions"/> at registration.
/// </summary>
/// <remarks>
/// One <c>GET api/internal/users/{id}</c> per distinct id, all in flight at
/// once. A project involves a handful of people — its Client, the Admin, the
/// Architect, the Project Manager — so that is a few small requests side by
/// side rather than a batch endpoint the User Service would have to grow.
/// </remarks>
public class HttpUserNameResolver : IUserNameResolver
{
    private const string ApiKeyHeader = "X-Internal-Api-Key";

    private readonly HttpClient _httpClient;
    private readonly InternalServiceOptions _internalServiceOptions;
    private readonly ILogger<HttpUserNameResolver> _logger;

    public HttpUserNameResolver(
        HttpClient httpClient,
        IOptions<InternalServiceOptions> internalServiceOptions,
        ILogger<HttpUserNameResolver> logger)
    {
        _httpClient = httpClient;
        _internalServiceOptions = internalServiceOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = userIds.Distinct().ToList();

        if (distinctIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var lookups = await Task.WhenAll(distinctIds.Select(id => LookUpAsync(id, cancellationToken)));

        return lookups
            .Where(lookup => lookup.Name is not null)
            .ToDictionary(lookup => lookup.Id, lookup => lookup.Name!);
    }

    private async Task<(Guid Id, string? Name)> LookUpAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/users/{userId}");
        request.Headers.Add(ApiKeyHeader, _internalServiceOptions.ApiKey);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException)
                                   && !cancellationToken.IsCancellationRequested)
        {
            // Unreachable, refused, or slower than the client's timeout. The
            // project is still shown, just without this name. A caller who has
            // hung up is not this case and is left to cancel the request.
            _logger.LogWarning(ex, "Could not reach the User Service to look up the name of account {UserId}.", userId);

            return (userId, null);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var user = await response.Content.ReadFromJsonAsync<InternalUserPayload>(cancellationToken);

                    return (userId, user is { FullName: { Length: > 0 } fullName } ? fullName : null);

                case HttpStatusCode.NotFound:
                    // An account that has since been removed. Not worth a
                    // warning: history outlives the people in it.
                    return (userId, null);

                case HttpStatusCode.Unauthorized:
                    // The User Service refused the shared key outright — an
                    // operator problem (the two services disagree on
                    // InternalService__ApiKey), not anything about this account.
                    _logger.LogError(
                        "The User Service refused the internal API key looking up account {UserId}; check "
                        + "InternalService__ApiKey matches on both services.", userId);

                    return (userId, null);

                default:
                    _logger.LogWarning(
                        "The User Service answered {StatusCode} looking up the name of account {UserId}.",
                        (int)response.StatusCode, userId);

                    return (userId, null);
            }
        }
    }

    /// <summary>
    /// Just the one field of <c>InternalUserResponse</c> this service needs. Web
    /// defaults, so <c>fullName</c> binds.
    /// </summary>
    private sealed record InternalUserPayload(string? FullName);
}
