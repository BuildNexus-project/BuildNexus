using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// Boots the real gateway host with a stub standing in for each of the five
/// services, so the routing table and the token check are exercised as written
/// rather than as described.
/// </summary>
/// <remarks>
/// Nothing here needs a database or a running stack: the gateway owns no data,
/// and every service behind it is a stub on a loopback port. The signing key is
/// supplied in memory rather than from User Secrets so the tests are
/// self-contained and run identically on any machine.
/// </remarks>
public class GatewayFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "BuildNexusAuth";
    public const string Audience = "BuildNexusServices";

    /// <summary>Long enough for HMAC-SHA256, and used nowhere but these tests.</summary>
    public const string SigningKey = "gateway-integration-test-signing-key-not-used-anywhere-else";

    public const string FrontendOrigin = "http://localhost:5173";

    public StubService User { get; } = StubService.Start("user");
    public StubService Project { get; } = StubService.Start("project");
    public StubService Design { get; } = StubService.Start("design");
    public StubService Construction { get; } = StubService.Start("construction");
    public StubService Payment { get; } = StubService.Start("payment");

    public IEnumerable<StubService> Stubs => [User, Project, Design, Construction, Payment];

    /// <summary>Forgets earlier traffic, so one test cannot observe another's.</summary>
    public void ResetStubs()
    {
        foreach (var stub in Stubs)
        {
            stub.Reset();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
                ["Cors:AllowedOrigins:0"] = FrontendOrigin,

                // Exactly the override docker-compose.yml performs, pointed at
                // stubs instead of containers. The keys are the contract: if a
                // cluster is renamed in appsettings.json and not here, the
                // matching test fails rather than silently proxying nowhere.
                ["ReverseProxy:Clusters:user:Destinations:primary:Address"] = User.Address,
                ["ReverseProxy:Clusters:project:Destinations:primary:Address"] = Project.Address,
                ["ReverseProxy:Clusters:design:Destinations:primary:Address"] = Design.Address,
                ["ReverseProxy:Clusters:construction:Destinations:primary:Address"] = Construction.Address,
                ["ReverseProxy:Clusters:payment:Destinations:primary:Address"] = Payment.Address
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var stub in Stubs)
        {
            await stub.DisposeAsync();
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
