using System.Net;
using System.Net.Http.Headers;

namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// The token half of US-25: the gateway validates before it proxies, and
/// forwards the token it validated.
/// </summary>
/// <remarks>
/// Every refusal here is asserted twice — the caller gets a 401, and the stub
/// behind the route received nothing. The status code alone would not
/// distinguish "refused at the gateway" from "proxied, and the service refused
/// it", which is the distinction the acceptance criterion is about.
/// </remarks>
[Collection(GatewayCollection.Name)]
public class TokenValidationTests
{
    /// <summary>One protected path per service.</summary>
    public static TheoryData<string> ProtectedPaths =>
    [
        "/api/users/me",
        "/api/projects",
        "/api/designs",
        "/api/construction",
        "/api/payments"
    ];

    private readonly GatewayFactory _factory;

    public TokenValidationTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.ResetStubs();
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task A_request_with_no_token_is_refused_and_never_proxied(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("not.a.token")]
    [InlineData("")]
    public async Task A_malformed_token_is_refused_and_never_proxied(string token)
    {
        var response = await Send("/api/users/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Fact]
    public async Task A_tampered_token_is_refused_and_never_proxied()
    {
        var response = await Send("/api/users/me", TestTokens.Tampered());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused_and_never_proxied()
    {
        // The case that catches a signing key drifting apart from the User
        // Service's: the claims are all correct, only the signature is foreign.
        var response = await Send("/api/users/me", TestTokens.SignedWithAnotherKey());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Fact]
    public async Task An_expired_token_is_refused_and_never_proxied()
    {
        var response = await Send("/api/users/me", TestTokens.Expired());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Fact]
    public async Task A_token_from_another_issuer_or_for_another_audience_is_refused()
    {
        var foreignIssuer = await Send("/api/users/me", TestTokens.WithIssuer("SomeoneElsesAuth"));
        var foreignAudience = await Send("/api/users/me", TestTokens.WithAudience("SomeoneElsesServices"));

        Assert.Equal(HttpStatusCode.Unauthorized, foreignIssuer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, foreignAudience.StatusCode);
        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    [Fact]
    public async Task A_refusal_carries_problem_details_and_a_challenge_header()
    {
        // The React client reads title and detail off every failure, so a
        // refusal here has to look like a refusal from a service.
        var response = await _factory.CreateClient().GetAsync("/api/users/me");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"title\"", body);
        Assert.Contains("\"detail\"", body);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task An_expired_token_is_called_out_by_name_in_the_challenge()
    {
        // Expiry is the one refusal the caller can act on, and saying so leaks
        // nothing they do not already hold.
        var response = await Send("/api/users/me", TestTokens.Expired());

        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString());
        Assert.Contains("expired", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task A_valid_token_is_forwarded_to_the_service_unchanged(string path)
    {
        // Each service validates the token itself rather than trusting that
        // something upstream did, so it has to arrive exactly as it was signed.
        var token = TestTokens.Valid();

        var response = await Send(path, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var received = _factory.Stubs.SelectMany(stub => stub.Received).Single();
        Assert.Equal($"Bearer {token}", received.Authorization);
    }

    [Fact]
    public async Task An_unrouted_path_is_refused_before_it_is_matched()
    {
        // The deny-by-default fallback covers requests that match no route at
        // all, so the route table cannot be enumerated from outside.
        var anonymous = await _factory.CreateClient().GetAsync("/api/nothing-owns-this");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var authenticated = await Send("/api/nothing-owns-this", TestTokens.Valid());
        Assert.Equal(HttpStatusCode.NotFound, authenticated.StatusCode);

        Assert.All(_factory.Stubs, stub => Assert.Empty(stub.Received));
    }

    private async Task<HttpResponseMessage> Send(string path, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(path);
    }
}
