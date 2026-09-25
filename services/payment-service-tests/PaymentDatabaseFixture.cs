using BuildNexus.PaymentService.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// Runs the real migrations against the local development database from
/// <c>infra/docker-compose.yml</c> and hands out real repositories over it.
/// </summary>
/// <remarks>
/// What is under test here is SQL, and the things worth asserting are the ones
/// only the real engine can answer: that <c>DECIMAL(15,2)</c> returns the exact
/// figure that went in rather than a drifted float, that the CHECK constraint
/// actually refuses a non-positive total, and that the newest-first ordering
/// holds at the column's real precision. A stub repository could show none of
/// that.
/// <para>
/// Requires the database to be running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait payment-db</c>. CI starts
/// it as its own step.
/// </para>
/// <para>
/// No web host is booted. Going through HTTP would only add a layer that could
/// fail for unrelated reasons.
/// </para>
/// </remarks>
public class PaymentDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Port 3311: the next free port after construction-db's 3310. Matches the
    /// service's own appsettings.json.
    /// </summary>
    public const string ConnectionString =
        "Server=localhost;Port=3311;Database=buildnexus_payment_db;User Id=buildnexus;Password=buildnexus-dev;";

    /// <summary>
    /// The first eight hex digits of every <c>project_id</c> a test in this run
    /// creates. Cleanup deletes exactly those rows and leaves anything a
    /// developer put in the database by hand alone.
    /// </summary>
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];

    public QuotationRepository QuotationRepository { get; private set; } = null!;

    /// <summary>
    /// The real <see cref="Data.ProjectOwnerRepository"/> over the same
    /// development database. The ownership check is what keeps one Client from
    /// reading another's cost estimates, and its idempotency is enforced by a
    /// primary key rather than by a check in C# — neither of which a stub could
    /// show.
    /// </summary>
    public ProjectOwnerRepository ProjectOwnerRepository { get; private set; } = null!;

    /// <summary>
    /// The real <see cref="Data.InvoiceRepository"/> over the same development
    /// database. Added for AC-2: the two CHECK constraints — the amount, and the
    /// status paired with its settlement date — are enforced by MySQL rather than
    /// by C#, so only the real engine can show that they refuse what they should.
    /// </summary>
    public InvoiceRepository InvoiceRepository { get; private set; } = null!;

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
                ["ConnectionStrings:PaymentDb"] = ConnectionString
            })
            .Build();

        var connectionFactory = new MySqlConnectionFactory(configuration);
        QuotationRepository = new QuotationRepository(connectionFactory);
        ProjectOwnerRepository = new ProjectOwnerRepository(connectionFactory);
        InvoiceRepository = new InvoiceRepository(connectionFactory);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the rows this run created, leaving the development database as it
    /// was found.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Both tables key off project_id from the same run-scoped namespace, so a
        // LIKE on the run prefix reaches every row this run created. No FKs
        // between them, so the order is a preference for tidiness, not a
        // constraint.
        foreach (var table in new[] { "quotations", "invoices", "project_owners" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE project_id LIKE @prefix;";
            command.Parameters.AddWithValue("@prefix", RunId + "-%");
            await command.ExecuteNonQueryAsync();
        }

        GC.SuppressFinalize(this);
    }
}
