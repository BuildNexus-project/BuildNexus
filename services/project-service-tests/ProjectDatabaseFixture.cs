using BuildNexus.ProjectService.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// Runs the real migrations against the local development database from
/// <c>infra/docker-compose.yml</c> and hands out a real
/// <see cref="ProjectRepository"/> over it.
/// </summary>
/// <remarks>
/// Every other test class in this project stubs its collaborators, which is
/// right for the decisions they cover — but a stub cannot reproduce what MySQL
/// actually does with a whole-second <c>DATETIME</c>, and that is precisely
/// where the status-history ordering defect hid. These tests exist to cover the
/// things only the real engine can answer.
/// <para>
/// Requires the database to be running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait project-db</c>. CI starts
/// it as its own step.
/// </para>
/// <para>
/// No web host is booted. What is under test is the SQL, so going through HTTP
/// would only add a layer that could fail for unrelated reasons.
/// </para>
/// </remarks>
public class ProjectDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Port 3307, not 3306: project-db publishes there because user-db already
    /// holds the standard port. Matches the service's own appsettings.json.
    /// </summary>
    public const string ConnectionString =
        "Server=localhost;Port=3307;Database=buildnexus_project_db;User Id=buildnexus;Password=buildnexus-dev;";

    /// <summary>
    /// Prefix every project a test creates, so cleanup can find them and leave
    /// anything else in the development database alone.
    /// </summary>
    public const string TestProjectPrefix = "test-";

    public ProjectRepository Repository { get; private set; } = null!;

    /// <summary>
    /// The outbox over the same database, so a test can check that an event
    /// enqueued by a project write actually committed with it.
    /// </summary>
    public OutboxRepository Outbox { get; private set; } = null!;

    public Task InitializeAsync()
    {
        // The production migration path, not a hand-written schema: DbUp records
        // what it has applied and runs only the rest, so this is safe against a
        // database that is empty, a story behind, or already current — and it
        // means a migration that does not actually run is a failing test rather
        // than a broken service on somebody's machine.
        DatabaseMigrator.Migrate(ConnectionString, NullLogger.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ProjectDb"] = ConnectionString
            })
            .Build();

        var connectionFactory = new MySqlConnectionFactory(configuration);

        Repository = new ProjectRepository(connectionFactory);
        Outbox = new OutboxRepository(connectionFactory);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the projects these tests created, leaving the development
    /// database as it was found. The history and outbox rows go with them —
    /// <c>fk_project_status_history_project</c> and
    /// <c>fk_project_outbox_events_project</c> both cascade on delete.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM projects WHERE name LIKE @prefix;";
        command.Parameters.AddWithValue("@prefix", TestProjectPrefix + "%");

        await command.ExecuteNonQueryAsync();

        GC.SuppressFinalize(this);
    }
}
