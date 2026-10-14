using System.Net;
using System.Text;
using BuildNexus.ConstructionService.Projects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="HttpProjectDirectoryClient"/> against a stand-in for the Project Service's
/// project list: what it sends, and how it turns each answer into a list of projects or an
/// admission that there is none.
/// </summary>
/// <remarks>
/// The handler stands in for the network, so these need no Project Service running. What is
/// under test is the client's own decisions — above all that an answer it could not get is
/// never reported as an empty list of projects, which would tell a Project Manager they have
/// nothing under way.
/// </remarks>
public class HttpProjectDirectoryClientTests
{
    private static readonly Guid Villa = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid Cottage = Guid.Parse("22222222-0000-4000-8000-000000000002");

    [Fact]
    public async Task Asks_for_the_project_list_as_the_caller()
    {
        var (client, handler) = ClientFor(_ => Json(HttpStatusCode.OK, "[]"));

        await client.ListVisibleProjectsAsync("the-callers-token", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/projects", request.Path);
        Assert.Equal("Bearer the-callers-token", request.Authorization);
    }

    [Fact]
    public async Task Returns_each_project_with_its_id_and_name()
    {
        var (client, _) = ClientFor(_ => Json(HttpStatusCode.OK, $$"""
            [
              {"id":"{{Villa}}","name":"Beachfront villa","status":"Construction","location":"Galle"},
              {"id":"{{Cottage}}","name":"Hilltop cottage","status":"Completed","location":"Kandy"}
            ]
            """));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal([Villa, Cottage], result.Ids());
        Assert.Equal("Beachfront villa", result.NameOf(Villa));
        Assert.Equal("Hilltop cottage", result.NameOf(Cottage));
    }

    [Fact]
    public async Task Keeps_a_completed_project_because_its_build_may_still_await_handover()
    {
        var (client, _) = ClientFor(_ => Json(HttpStatusCode.OK, $$"""
            [{"id":"{{Cottage}}","name":"Hilltop cottage","status":"Completed"}]
            """));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.Equal([Cottage], result.Ids());
    }

    [Fact]
    public async Task A_project_it_was_not_told_about_has_no_name()
    {
        var (client, _) = ClientFor(_ => Json(HttpStatusCode.OK, $$"""
            [{"id":"{{Villa}}","name":"Beachfront villa","status":"Construction"}]
            """));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.Null(result.NameOf(Cottage));
    }

    [Fact]
    public async Task An_empty_list_is_an_answer_not_a_failure()
    {
        var (client, _) = ClientFor(_ => Json(HttpStatusCode.OK, "[]"));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Empty(result.Projects);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Any_answer_other_than_a_list_is_unavailable_not_an_empty_list(HttpStatusCode status)
    {
        var (client, _) = ClientFor(_ => new HttpResponseMessage(status));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task A_project_service_that_cannot_be_reached_is_unavailable()
    {
        var (client, _) = ClientFor(_ => throw new HttpRequestException("connection refused"));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task A_project_service_that_is_too_slow_is_unavailable()
    {
        var (client, _) = ClientFor(_ => throw new TaskCanceledException("timed out"));

        var result = await client.ListVisibleProjectsAsync("token", CancellationToken.None);

        Assert.False(result.IsAvailable);
    }

    // ------------------------------------------------------------ helpers ----

    private static (HttpProjectDirectoryClient Client, StubHandler Handler) ClientFor(
        Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new StubHandler(answer);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://project-service/") };

        return (new HttpProjectDirectoryClient(httpClient, NullLogger<HttpProjectDirectoryClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Authorization);

    /// <summary>Answers every request from a delegate, and records what was asked.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answer = answer;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString()));

            return Task.FromResult(_answer(request));
        }
    }
}
