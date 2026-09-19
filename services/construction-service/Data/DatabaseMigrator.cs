using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Brings the database up to date at startup by running the numbered scripts in
/// <c>Migrations/</c>.
/// </summary>
/// <remarks>
/// Set up the same way as every other BuildNexus service: DbUp records what it
/// has applied in a <c>schemaversions</c> table and runs only the rest, so
/// starting the service is safe whether the database is empty, a story behind,
/// or already current. It runs the plain <c>.sql</c> files we write by hand — it
/// does not generate SQL or sit in front of a query; the ADO.NET data access is
/// untouched.
/// </remarks>
public static class DatabaseMigrator
{
    /// <summary>Applies any scripts that have not run yet.</summary>
    /// <remarks>
    /// Does not create the database itself — <c>buildnexus_construction_db</c>
    /// is expected to already exist, the way <c>MYSQL_DATABASE</c> creates it in
    /// <c>infra/docker-compose.yml</c>. The <c>buildnexus</c> account is scoped
    /// to its own database and cannot reach MySQL's <c>mysql</c> schema, which
    /// is what DbUp's <c>EnsureDatabase</c> would need.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A script failed. Thrown so the service stops rather than serving requests
    /// against a schema it does not match.
    /// </exception>
    public static void Migrate(string connectionString, ILogger logger)
    {
        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogError(
                result.Error,
                "Database migration failed on script {Script}.",
                result.ErrorScript?.Name ?? "(unknown)");

            throw new InvalidOperationException(
                $"Database migration failed on '{result.ErrorScript?.Name}'.", result.Error);
        }

        if (result.Scripts.Any())
        {
            logger.LogInformation(
                "Applied {Count} database migration(s): {Scripts}.",
                result.Scripts.Count(),
                string.Join(", ", result.Scripts.Select(script => script.Name)));

            return;
        }

        logger.LogInformation("Database schema is already up to date.");
    }
}
