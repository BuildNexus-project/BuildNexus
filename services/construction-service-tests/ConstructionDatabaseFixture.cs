using BuildNexus.ConstructionService.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// Runs the real migrations against the local development database from
/// <c>infra/docker-compose.yml</c> and hands out a real
/// <see cref="MilestoneSetupRepository"/> over it.
/// </summary>
/// <remarks>
/// <c>INSERT IGNORE</c> against a real unique index is the point of US-23's
/// idempotency: a stub that just skips a duplicate key cannot show that MySQL
/// actually does, or that a second call reports zero rows affected. These tests
/// cover the things only the real engine can answer.
/// <para>
/// Requires the database to be running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait construction-db</c>. CI
/// starts it as its own step.
/// </para>
/// <para>
/// No web host is booted. What is under test is the SQL, so going through HTTP
/// would only add a layer that could fail for unrelated reasons.
/// </para>
/// </remarks>
public class ConstructionDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Port 3310: user-db has 3306, project-db 3307, design-db 3309, so
    /// construction-db publishes there. Matches the service's own appsettings.json.
    /// </summary>
    public const string ConnectionString =
        "Server=localhost;Port=3310;Database=buildnexus_construction_db;User Id=buildnexus;Password=buildnexus-dev;";

    /// <summary>
    /// The first eight hex digits of every <c>project_id</c> a test in this run
    /// creates. Cleanup deletes exactly those rows and leaves anything a
    /// developer put in the database by hand alone.
    /// </summary>
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];

    public MilestoneSetupRepository Repository { get; private set; } = null!;

    /// <summary>Builds a <c>project_id</c> in this run's namespace, so cleanup can find it.</summary>
    public Guid ProjectId(string suffix) => Guid.Parse($"{RunId}-0000-4000-8000-{suffix.PadLeft(12, '0')}");

    public Task InitializeAsync()
    {
        // The production migration path, not a hand-written schema: DbUp records
        // what it has applied and runs only the rest, so this is safe against a
        // database that is empty, a story behind, or already current.
        DatabaseMigrator.Migrate(ConnectionString, NullLogger.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ConstructionDb"] = ConnectionString
            })
            .Build();

        Repository = new MilestoneSetupRepository(new MySqlConnectionFactory(configuration));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the placeholders this run created, leaving the development
    /// database as it was found.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM milestone_setups WHERE project_id LIKE @prefix;";
        command.Parameters.AddWithValue("@prefix", RunId + "-%");

        await command.ExecuteNonQueryAsync();

        GC.SuppressFinalize(this);
    }
}
