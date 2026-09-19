using BuildNexus.DesignService.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// Runs the real migrations against the local development database from
/// <c>infra/docker-compose.yml</c> and hands out a real
/// <see cref="DesignDocumentRepository"/> over it.
/// </summary>
/// <remarks>
/// The version-number increment is a MAX + 1 read inside a transaction with a
/// unique constraint behind it — a stub cannot show that it actually holds
/// under a real engine, or that the bytes survive a round trip through a
/// <c>LONGBLOB</c>. These tests cover the things only real MySQL can answer.
/// <para>
/// Requires the database to be running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait design-db</c>. CI starts
/// it as its own step.
/// </para>
/// <para>
/// No web host is booted. What is under test is the SQL, so going through HTTP
/// would only add a layer that could fail for unrelated reasons.
/// </para>
/// </remarks>
public class DesignDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Port 3309: user-db has 3306, project-db has 3307, so design-db publishes
    /// there. Matches the service's own appsettings.json.
    /// </summary>
    public const string ConnectionString =
        "Server=localhost;Port=3309;Database=buildnexus_design_db;User Id=buildnexus;Password=buildnexus-dev;";

    /// <summary>
    /// Prefix every document a test creates, so cleanup can find them and leave
    /// anything else in the development database alone.
    /// </summary>
    public const string TestDocumentPrefix = "test-";

    public DesignDocumentRepository Repository { get; private set; } = null!;

    /// <summary>For reading back what <see cref="Repository"/> wrote to the outbox alongside a decision.</summary>
    public OutboxRepository Outbox { get; private set; } = null!;

    /// <summary>The aggregate report queries (US-20), over the same live database.</summary>
    public DesignReportRepository Reports { get; private set; } = null!;

    public Task InitializeAsync()
    {
        // The production migration path, not a hand-written schema: DbUp records
        // what it has applied and runs only the rest, so this is safe against a
        // database that is empty, a story behind, or already current.
        DatabaseMigrator.Migrate(ConnectionString, NullLogger.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DesignDb"] = ConnectionString
            })
            .Build();

        var connectionFactory = new MySqlConnectionFactory(configuration);
        Repository = new DesignDocumentRepository(connectionFactory);
        Outbox = new OutboxRepository(connectionFactory);
        Reports = new DesignReportRepository(connectionFactory);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the documents these tests created, leaving the development
    /// database as it was found. The version rows go with them —
    /// <c>fk_design_document_versions_document</c> cascades on delete.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM design_documents WHERE name LIKE @prefix;";
        command.Parameters.AddWithValue("@prefix", TestDocumentPrefix + "%");

        await command.ExecuteNonQueryAsync();

        GC.SuppressFinalize(this);
    }
}
