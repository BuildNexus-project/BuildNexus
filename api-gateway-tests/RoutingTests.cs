using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// The routing half of US-25: every public path prefix reaches the service that
/// owns it, and nothing else reaches a service at all.
/// </summary>
[Collection(GatewayCollection.Name)]
public class RoutingTests
{
    private readonly GatewayFactory _factory;

    public RoutingTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.ResetStubs();
    }

    /// <summary>
    /// The routing table as the frontend sees it. Each prefix is listed with a
    /// path below it as well, so a route is not passed by matching only its own
    /// first segment.
    /// </summary>
    public static TheoryData<string, string> RoutedPaths => new()
    {
        { "/api/users", "user" },
        { "/api/users/me", "user" },
        { "/api/projects", "project" },
        { "/api/projects/8f14e45f/tasks", "project" },
        { "/api/designs", "design" },
        { "/api/designs/12/revisions", "design" },
        { "/api/construction", "construction" },
        { "/api/construction/site-logs", "construction" },
        { "/api/payments", "payment" },
        { "/api/payments/invoices/7", "payment" }
    };

    [Theory]
    [MemberData(nameof(RoutedPaths))]
    public async Task Each_path_prefix_reaches_the_service_that_owns_it(string path, string expectedService)
    {
        var response = await SignedInClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StubResponse>();
        Assert.Equal(expectedService, body!.Service);
    }

    [Theory]
    [MemberData(nameof(RoutedPaths))]
    public async Task The_path_arrives_at_the_service_unchanged(string path, string expectedService)
    {
        // The services own the whole /api/... path, so the gateway must not
        // rewrite or strip the prefix on its way through.
        var response = await SignedInClient().GetAsync(path);

        var body = await response.Content.ReadFromJsonAsync<StubResponse>();
        Assert.Equal(path, body!.Path);
        Assert.Equal(expectedService, body.Service);
    }

    [Fact]
    public async Task All_five_services_are_reachable()
    {
        // The acceptance criterion names five services, so assert five, not
        // "the ones that happen to be configured".
        var client = SignedInClient();

        var reached = new List<string>();
        foreach (var path in new[]
                 {
                     "/api/users", "/api/projects", "/api/designs",
                     "/api/construction", "/api/payments"
                 })
        {
            var body = await (await client.GetAsync(path)).Content.ReadFromJsonAsync<StubResponse>();
            reached.Add(body!.Service);
        }

        Assert.Equal(
            new[] { "user", "project", "design", "construction", "payment" },
            reached);
    }

    [Fact]
    public async Task The_login_route_reaches_the_user_service_without_a_token()
    {
        // Registration and login are how a caller obtains a token in the first
        // place, so they cannot themselves require one.
        var response = await _factory.CreateClient().PostAsync("/api/auth/login", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StubResponse>();
        Assert.Equal("user", body!.Service);
        Assert.Equal("/api/auth/login", body.Path);
    }

    [Fact]
    public async Task An_unrouted_path_reaches_no_service()
    {
        var response = await SignedInClient().GetAsync("/api/nothing-owns-this");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Fact]
    public async Task The_health_probe_answers_from_the_gateway_itself()
    {
        // It must not depend on a service being up, so nothing may be proxied.
        var response = await _factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    private HttpClient SignedInClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Valid());
        return client;
    }
}
