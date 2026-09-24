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

    /// <summary>
    /// The real <see cref="Data.MilestoneRepository"/> over the same
    /// development database. Added for US-12: the milestone tests need to
    /// exercise the ADO.NET SQL — the gate check against
    /// <c>milestone_setups</c>, the duplicate-key translation, the aggregate
    /// query — which only a real engine can answer.
    /// </summary>
    public MilestoneRepository MilestoneRepository { get; private set; } = null!;

    /// <summary>
    /// The real <see cref="Data.ProjectOwnerRepository"/> over the same
    /// development database. Added for US-13: the ownership check is what keeps
    /// one Client from reading another's build progress, so its SQL — the
    /// <c>INSERT IGNORE</c> that absorbs a redelivered <c>ProjectCreated</c>,
    /// and the <c>EXISTS</c> that answers the pair — is worth exercising
    /// against the real engine rather than a stub.
    /// </summary>
    public ProjectOwnerRepository ProjectOwnerRepository { get; private set; } = null!;

    /// <summary>
    /// The real <see cref="Data.ConstructionPhaseRepository"/> over the same
    /// development database. Added for US-14: the three transitions are gated by
    /// SQL that reads other tables in the same transaction — the design-approval
    /// marker, the milestone tally — and the start gate is enforced by a primary
    /// key rather than by a check in C#. None of that can be shown against a stub.
    /// </summary>
    public ConstructionPhaseRepository ConstructionPhaseRepository { get; private set; } = null!;

    /// <summary>
    /// The real <see cref="Data.OutboxRepository"/> over the same development
    /// database. Added for US-14: a transition's event is enqueued inside the
    /// transition's own transaction, so the only honest way to assert that it was
    /// announced — and that a refused transition announced nothing — is to read the
    /// outbox table back after the fact.
    /// </summary>
    public OutboxRepository OutboxRepository { get; private set; } = null!;

    /// <summary>Builds a <c>project_id</c> in this run's namespace, so cleanup can find it.</summary>
    public Guid ProjectId(string suffix) => Guid.Parse($"{RunId}-0000-4000-8000-{suffix.PadLeft(12, '0')}");

    /// <summary>
    /// Plants a <c>milestone_setups</c> row for a project — the local marker
    /// the <c>DesignApproved</c> consumer would leave (US-23), and the row
    /// <see cref="Data.MilestoneRepository.CreateAsync"/> and
    /// <see cref="Data.MilestoneRepository.GetProgressForProjectAsync"/>
    /// check to answer "has the design been approved?".
    /// </summary>
    public Task PlantApprovedDesignAsync(Guid projectId) =>
        Repository.CreatePlaceholderIfAbsentAsync(
            projectId,
            sourceDocumentId: Guid.NewGuid(),
            sourceEventId: Guid.NewGuid(),
            approvedAtUtc: DateTime.UtcNow);

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

        var connectionFactory = new MySqlConnectionFactory(configuration);
        Repository = new MilestoneSetupRepository(connectionFactory);
        MilestoneRepository = new MilestoneRepository(connectionFactory);
        ProjectOwnerRepository = new ProjectOwnerRepository(connectionFactory);
        ConstructionPhaseRepository = new ConstructionPhaseRepository(connectionFactory);
        OutboxRepository = new OutboxRepository(connectionFactory);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the placeholders and milestones this run created, leaving the
    /// development database as it was found.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Every table uses project_id from the same run-scoped namespace, so a
        // LIKE on the run prefix reaches every row this run created. No FKs
        // between them, so the order does not matter — this order is a
        // preference for tidiness, not a constraint.
        foreach (var table in new[]
                 {
                     // The outbox leads: its rows point at construction_phases with
                     // ON DELETE CASCADE, so deleting them explicitly first keeps
                     // this cleanup readable rather than relying on the cascade.
                     "construction_outbox_events", "construction_phases",
                     "construction_milestones", "milestone_setups", "project_owners"
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE project_id LIKE @prefix;";
            command.Parameters.AddWithValue("@prefix", RunId + "-%");
            await command.ExecuteNonQueryAsync();
        }

        GC.SuppressFinalize(this);
    }
}