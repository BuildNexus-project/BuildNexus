using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// A stand-in for one of the five services: a real HTTP server on a loopback
/// port that answers every path with its own name and records what it was sent.
/// </summary>
/// <remarks>
/// Four of the five services do not exist yet, and the gateway's job is not to
/// know anything about them beyond an address. Standing up a stub per cluster
/// lets the routing table be tested for what it actually claims — that a given
/// path prefix reaches a given service and arrives intact — without waiting on
/// those services to be written, and without a test depending on a database.
/// A request that never arrives is as much a result as one that does, which is
/// how "refused before it was proxied" is asserted.
/// </remarks>
public sealed class StubService : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<ReceivedRequest> _received;

    /// <summary>The cluster this stub stands in for, echoed back in every response.</summary>
    public string Name { get; }

    /// <summary>Loopback address the gateway is pointed at, e.g. http://127.0.0.1:53124.</summary>
    public string Address { get; }

    /// <summary>Every request this stub has been sent since the last <see cref="Reset"/>.</summary>
    public IReadOnlyCollection<ReceivedRequest> Received => _received;

    private StubService(string name, WebApplication app, string address, ConcurrentQueue<ReceivedRequest> received)
    {
        Name = name;
        _app = app;
        Address = address;
        _received = received;
    }

    public static StubService Start(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Port 0 lets the OS pick a free one, so a developer's own services --
        // or a second test run -- never collide with these.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();

        // The queue is captured directly rather than reached through the
        // StubService, which cannot exist until the server has bound a port.
        var received = new ConcurrentQueue<ReceivedRequest>();

        app.Map("/{**path}", (HttpContext context) =>
        {
            received.Enqueue(new ReceivedRequest(
                context.Request.Path.Value ?? string.Empty,
                context.Request.Method,
                context.Request.Headers.Authorization.ToString()));

            return Results.Ok(new StubResponse(name, context.Request.Path.Value ?? string.Empty));
        });

        app.Start();

        var address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        return new StubService(name, app, address, received);
    }

    /// <summary>Forgets earlier traffic, so one test cannot see another's.</summary>
    public void Reset() => _received.Clear();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>What a stub was sent, as far as these tests care.</summary>
public record ReceivedRequest(string Path, string Method, string Authorization);

/// <summary>What a stub answers with, so a test can tell which one replied.</summary>
public record StubResponse(string Service, string Path);
