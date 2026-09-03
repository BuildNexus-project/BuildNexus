using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace BuildNexus.UserService.Data;

/// <summary>
/// Brings the database up to date at startup by running the numbered scripts in
/// <c>Migrations/</c>.
/// </summary>
/// <remarks>
/// DbUp records every script it has run in a <c>schemaversions</c> table and
/// applies only the ones missing, so starting the service is safe whether the
/// database is empty, one story behind, or already current.
/// <para>
/// This replaces mounting the scripts into the MySQL container's
/// <c>docker-entrypoint-initdb.d</c>, which only ran them when the data volume
/// was brand new — so every schema change after the first needed the volume,
/// and all its data, thrown away.
/// </para>
/// <para>
/// DbUp runs the same plain <c>.sql</c> files we already write by hand. It does
/// not generate SQL, map objects or sit in front of a query: the ADO.NET data
/// access is untouched.
/// </para>
/// </remarks>
public static class DatabaseMigrator
{
    /// <summary>
    /// Applies any scripts that have not run yet.
    /// </summary>
    /// <remarks>
    /// Does not create the database itself — <c>buildnexus_user_db</c> is
    /// expected to already exist, the way <c>MYSQL_DATABASE</c> creates it in
    /// <c>infra/docker-compose.yml</c>. Creating it here would need DbUp's
    /// <c>EnsureDatabase</c> helper, which checks for the database by opening a
    /// separate connection to MySQL's own <c>mysql</c> schema — something the
    /// <c>buildnexus</c> account cannot do, since it is granted access to only
    /// its own database. That scoping is deliberate (one schema per service,
    /// enforced at the account level, not just by convention), so the fix is to
    /// not need the broader access rather than to grant it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A script failed. Thrown so the service stops rather than serving requests
    /// against a schema it does not match.
    /// </exception>
    public static void Migrate(string connectionString, ILogger logger)
    {
        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionString)
            // Scripts are embedded in the assembly, so a published container
            // carries them and cannot drift from the code that expects them.
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

        LogOutcome(result, logger);
    }

    /// <summary>
    /// DbUp's own log is silenced above so its output goes through the service's
    /// logger in one line, rather than straight to the console in another format.
    /// </summary>
    private static void LogOutcome(DatabaseUpgradeResult result, ILogger logger)
    {
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
