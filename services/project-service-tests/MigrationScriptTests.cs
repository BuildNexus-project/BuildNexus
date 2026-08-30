using System.Reflection;
using System.Text.RegularExpressions;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// Guards the migration scripts themselves.
/// </summary>
/// <remarks>
/// DbUp finds scripts by looking for embedded resources, so a script added to
/// <c>Migrations/</c> without the build embedding it is silently skipped — the
/// service starts, and the missing column only surfaces as a query failure
/// later. These tests turn that into a failing build instead. No database
/// needed: they read the assembly, not MySQL.
/// </remarks>
public class MigrationScriptTests
{
    private static readonly Assembly ServiceAssembly = typeof(DatabaseMigrator).Assembly;

    [Fact]
    public void Every_migration_script_is_embedded_in_the_assembly()
    {
        Assert.NotEmpty(ScriptNames());
    }

    [Theory]
    [InlineData("001_create_projects_table.sql")]
    [InlineData("002_add_project_status_history.sql")]
    public void The_known_scripts_are_present(string fileName)
    {
        Assert.Contains(ScriptNames(), name => name.EndsWith(fileName, StringComparison.Ordinal));
    }

    [Fact]
    public void Script_names_sort_into_the_order_they_must_run_in()
    {
        // DbUp runs scripts in name order, so a later ALTER only lands after the
        // CREATE it depends on. Zero-padded numbers keep that true past nine.
        var names = ScriptNames();

        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal), names);
    }

    [Fact]
    public void No_script_switches_database()
    {
        // A USE or CREATE DATABASE inside a script would take effect outside the
        // schema this service owns. The connection string names the database;
        // scripts must assume they are already in it.
        //
        // Matched as statements against the SQL with its comments stripped, not
        // as a substring of the whole file: a plain case-insensitive search for
        // "USE " also finds it inside "a house with no floors", and a test that
        // fails on the prose in a comment teaches people to reword comments
        // rather than to stop switching database.
        foreach (var resource in ScriptNames())
        {
            var sql = StripComments(ReadScript(resource));

            Assert.DoesNotMatch(@"(?im)^\s*USE\s", sql);
            Assert.DoesNotMatch(@"(?i)\bCREATE\s+DATABASE\b", sql);
        }
    }

    [Fact]
    public void No_script_reaches_into_another_services_schema()
    {
        // One schema per service, enforced here as well as by convention: a
        // foreign key into the User Service's users table would couple the two
        // databases and is exactly what projects.client_id deliberately is not.
        foreach (var resource in ScriptNames())
        {
            // Comments stripped for the same reason as above: this must fail on
            // a foreign key, not on a comment explaining why there is not one.
            var sql = StripComments(ReadScript(resource));

            Assert.DoesNotContain("REFERENCES users", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buildnexus_user_db", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_script_that_created_the_projects_table_is_left_exactly_as_it_shipped()
    {
        // 'Pending' was the only status US-05 defined, and 001 still says so.
        // DbUp has already recorded this script as run, so editing it would
        // change nothing on any database that exists — US-06 widens the
        // constraint in 002 instead.
        var sql = ReadScript(Script("001_create_projects_table.sql"));

        Assert.Contains("status IN ('Pending')", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_constraint_allows_the_whole_lifecycle()
    {
        // The five statuses US-06 defines, replacing the single-value check
        // from 001. A project cannot be moved to a status the column refuses,
        // so this constraint and the enum have to agree.
        var sql = ReadScript(Script("002_add_project_status_history.sql"));

        Assert.Contains(
            "status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed')",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_status_the_service_can_produce_is_allowed_by_the_schema()
    {
        // Written over the enum rather than over today's five names: a status
        // added in a later story without a matching migration would otherwise
        // only surface as a constraint violation the first time somebody tried
        // to use it.
        var sql = ReadScript(Script("002_add_project_status_history.sql"));

        var missing = Enum.GetNames<ProjectStatus>()
            .Where(status => !sql.Contains($"'{status}'", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These statuses exist in ProjectStatus but no migration allows them in the database: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void The_status_history_table_records_who_made_each_change()
    {
        // The audit trail is only worth keeping if it says who and when, not
        // just what.
        var sql = StripComments(ReadScript(Script("002_add_project_status_history.sql")));

        Assert.Contains("CREATE TABLE IF NOT EXISTS project_status_history", sql, StringComparison.Ordinal);

        foreach (var column in (string[])["from_status", "to_status", "changed_by_user_id", "changed_by_role", "changed_at"])
        {
            Assert.Contains(column, sql, StringComparison.Ordinal);
        }
    }

    private static string Script(string fileName) =>
        ScriptNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

    /// <summary>
    /// Drops <c>--</c> line comments, so the checks above read the SQL a script
    /// actually runs rather than the prose explaining it.
    /// </summary>
    private static string StripComments(string sql) =>
        Regex.Replace(sql, "--.*$", string.Empty, RegexOptions.Multiline);

    private static string ReadScript(string resourceName)
    {
        using var stream = ServiceAssembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static List<string> ScriptNames() =>
        [.. ServiceAssembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)];
}
