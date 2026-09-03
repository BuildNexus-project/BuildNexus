using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// The gateway refuses to start on a signing key it cannot validate with,
/// rather than starting and turning every proxied request into a 401.
/// </summary>
/// <remarks>
/// A gateway that boots with a bad key looks healthy to a deployment and fails
/// only under real traffic, where the cause reads as "everyone is signed out"
/// rather than "the key is wrong". Failing at startup puts the error where
/// someone is already looking.
/// </remarks>
public class StartupConfigurationTests
{
    [Theory]
    // A missing key, and one too short for HMAC-SHA256.
    [InlineData("")]
    [InlineData("too-short")]
    public void The_gateway_refuses_to_start_without_a_usable_signing_key(string key)
    {
        var exception = Record.Exception(() => CreateHost("Jwt:SigningKey", key));

        Assert.NotNull(exception);
        Assert.Contains("SigningKey", Flatten(exception));
    }

    [Theory]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    public void The_gateway_refuses_to_start_without_the_rest_of_the_shared_convention(string setting)
    {
        var exception = Record.Exception(() => CreateHost(setting, string.Empty));

        Assert.NotNull(exception);
        Assert.Contains(setting.Split(':')[1], Flatten(exception));
    }

    private static void CreateHost(string setting, string? value)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Issuer"] = GatewayFactory.Issuer,
                        ["Jwt:Audience"] = GatewayFactory.Audience,
                        ["Jwt:SigningKey"] = GatewayFactory.SigningKey
                    });
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { [setting] = value });
                });
            });

        // The host is built lazily; asking for a client is what starts it.
        factory.CreateClient();
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
