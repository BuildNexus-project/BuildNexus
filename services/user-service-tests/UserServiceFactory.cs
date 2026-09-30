using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// Boots the real User Service host against the local development database from
/// <c>infra/docker-compose.yml</c>, so the tests exercise the actual ADO.NET
/// code path rather than a substitute.
/// </summary>
/// <remarks>
/// Requires the stack to be running: <c>cd infra &amp;&amp; docker compose up -d user-db</c>.
/// Configuration is supplied here rather than read from User Secrets so the
/// tests are self-contained and run identically on any machine.
/// </remarks>
public class UserServiceFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The environment variable that says which host port <c>user-db</c> is on. Unset
    /// means the standard <see cref="DefaultPort"/>, which is what CI publishes.
    /// </summary>
    public const string PortVariable = "USER_DB_PORT";

    /// <summary>MySQL's standard port, and where <c>infra/docker-compose.yml</c> publishes <c>user-db</c>.</summary>
    public const string DefaultPort = "3306";

    /// <summary>
    /// The development database, on <see cref="PortVariable"/> or <see cref="DefaultPort"/>.
    /// </summary>
    /// <remarks>
    /// The port is not a constant because it is the one thing that differs between
    /// machines. A native MySQL service already holds 3306 on some developers' machines,
    /// and a local, git-ignored <c>infra/docker-compose.override.yml</c> moves
    /// <c>user-db</c> to 3308 there; the tests then reached that MySQL instead of the
    /// container and every one of them failed at login. Everything else — database name,
    /// user, password — is the same published development value everywhere, so it stays
    /// fixed. CI sets nothing and gets 3306.
    /// <para>
    /// <c>user-service-tests/local.runsettings</c> (git-ignored, optional) is the place to
    /// set it for a machine, so it does not have to be exported in every shell.
    /// </para>
    /// </remarks>
    public static readonly string ConnectionString =
        $"Server=localhost;Port={Environment.GetEnvironmentVariable(PortVariable) ?? DefaultPort};"
        + "Database=buildnexus_user_db;User Id=buildnexus;Password=buildnexus-dev;";

    /// <summary>Prefix every account a test creates, so cleanup can find them.</summary>
    public const string TestEmailPrefix = "test-";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development so the bootstrap Admin seeder runs, as it does locally.
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserDb"] = ConnectionString,
                ["Jwt:Issuer"] = "BuildNexusAuth",
                ["Jwt:Audience"] = "BuildNexusServices",
                ["Jwt:SigningKey"] = "integration-test-signing-key-not-used-anywhere-else",
                ["Jwt:AccessTokenLifetimeMinutes"] = "60",
                ["InternalService:ApiKey"] = "integration-test-internal-api-key-not-used-anywhere-else"
            });
        });
    }

    /// <summary>
    /// Removes the accounts these tests created, leaving the development
    /// database as it was found. The seeded Admin is left in place — it is not
    /// test data, and the seeder only creates it when no Admin exists.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await using (var connection = new MySqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM users WHERE email LIKE @prefix;";
            command.Parameters.AddWithValue("@prefix", TestEmailPrefix + "%");
            await command.ExecuteNonQueryAsync();
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
