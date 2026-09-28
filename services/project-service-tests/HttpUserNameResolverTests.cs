using System.Net;
using System.Text;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="HttpUserNameResolver"/> against a stand-in for the User Service's
/// internal lookup: what it sends, what it makes of each answer, and that no
/// answer — however bad — stops the others being used.
/// </summary>
/// <remarks>
/// The handler stands in for the network, so these need no User Service running.
/// What is under test is the resolver's own decisions: which requests it makes,
/// and how it turns a status code or a dropped connection into "a name" or
/// "no name".
/// </remarks>
public class HttpUserNameResolverTests
{
    private const string ApiKey = "a-shared-key-that-is-at-least-thirty-two-bytes";

    private static readonly Guid AnnId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid BenId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CyId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Returns_the_name_the_user_service_gives()
    {
        var (resolver, _) = ResolverFor(_ => Json(HttpStatusCode.OK, """{"id":"x","fullName":"Ann Silva","email":"ann@example.com"}"""));

        var names = await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        Assert.Equal("Ann Silva", names[AnnId]);
    }

    [Fact]
    public async Task Asks_the_internal_endpoint_and_presents_the_shared_key()
    {
        var (resolver, handler) = ResolverFor(_ => Json(HttpStatusCode.OK, """{"fullName":"Ann Silva"}"""));

        await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"/api/internal/users/{AnnId}", request.Path);
        // The key and nothing else: this call is made for a Client too, who has no
        // token that would be accepted, so none is forwarded.
        Assert.Equal(ApiKey, request.ApiKey);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task Asks_about_each_person_once_however_often_they_appear()
    {
        var (resolver, handler) = ResolverFor(request => Json(HttpStatusCode.OK, $$"""{"fullName":"Name for {{request.Path}}"}"""));

        // A Project Manager who both moved the project and is assigned to it is
        // passed twice; there is one account to look up.
        var names = await resolver.ResolveNamesAsync([AnnId, BenId, AnnId], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public async Task Makes_no_request_when_there_is_nobody_to_look_up()
    {
        var (resolver, handler) = ResolverFor(_ => Json(HttpStatusCode.OK, """{"fullName":"unused"}"""));

        var names = await resolver.ResolveNamesAsync([], CancellationToken.None);

        Assert.Empty(names);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Leaves_out_an_account_the_user_service_does_not_know()
    {
        // History outlives the people in it: a removed account is a missing name,
        // not an error.
        var (resolver, _) = ResolverFor(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var names = await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        Assert.Empty(names);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Leaves_out_a_name_when_the_user_service_answers_badly(HttpStatusCode status)
    {
        // Including a refused key, which is the two services disagreeing about
        // configuration: logged loudly, but still no reason to fail the page.
        var (resolver, _) = ResolverFor(_ => new HttpResponseMessage(status));

        var names = await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        Assert.Empty(names);
    }

    [Theory]
    [InlineData("""{"fullName":""}""")]
    [InlineData("""{"fullName":null}""")]
    [InlineData("""{}""")]
    public async Task Leaves_out_a_reply_that_carries_no_name(string body)
    {
        var (resolver, _) = ResolverFor(_ => Json(HttpStatusCode.OK, body));

        var names = await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task Leaves_out_a_name_when_the_user_service_cannot_be_reached()
    {
        var (resolver, _) = ResolverFor(_ => throw new HttpRequestException("Connection refused"));

        var names = await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task Leaves_out_a_name_when_the_user_service_is_too_slow()
    {
        // HttpClient reports its own timeout as a TaskCanceledException, which is
        // not the caller hanging up.
        var (resolver, _) = ResolverFor(_ => throw new TaskCanceledException("The request timed out."));

        var names = await resolver.ResolveNamesAsync([AnnId], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task Still_uses_the_names_it_found_when_another_lookup_fails()
    {
        var (resolver, _) = ResolverFor(request => request.Path.EndsWith(BenId.ToString())
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(HttpStatusCode.OK, $$"""{"fullName":"Name for {{request.Path[^36..]}}"}"""));

        var names = await resolver.ResolveNamesAsync([AnnId, BenId, CyId], CancellationToken.None);

        Assert.Equal($"Name for {AnnId}", names[AnnId]);
        Assert.Equal($"Name for {CyId}", names[CyId]);
        Assert.False(names.ContainsKey(BenId));
    }

    [Fact]
    public async Task Does_not_swallow_the_caller_hanging_up()
    {
        // A cancelled request is the caller's decision, not a missing name — it
        // has to reach the framework so the work stops. HttpClient itself refuses
        // to send on a token that is already cancelled.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var (resolver, _) = ResolverFor(_ => Json(HttpStatusCode.OK, """{"fullName":"Ann Silva"}"""));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveNamesAsync([AnnId], cancelled.Token));
    }

    // ------------------------------------------------------------- setup ----

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (HttpUserNameResolver Resolver, StubHandler Handler) ResolverFor(
        Func<RecordedRequest, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://user-service:8080/") };

        var resolver = new HttpUserNameResolver(
            client,
            Options.Create(new InternalServiceOptions { ApiKey = ApiKey }),
            NullLogger<HttpUserNameResolver>.Instance);

        return (resolver, handler);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? ApiKey, string? Authorization);

    /// <summary>
    /// Answers every request with whatever the test's function returns — or lets
    /// it throw, to stand in for a connection that never happened — and records
    /// what was asked.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _respond;
        private readonly List<RecordedRequest> _requests = [];

        public StubHandler(Func<RecordedRequest, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        /// <summary>Everything asked so far. Requests run side by side, so it is read under a lock.</summary>
        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-Internal-Api-Key", out var keys) ? keys.Single() : null,
                request.Headers.Authorization?.ToString());

            lock (_requests)
            {
                _requests.Add(recorded);
            }

            return Task.FromResult(_respond(recorded));
        }
    }
}
