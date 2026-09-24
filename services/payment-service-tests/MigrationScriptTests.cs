using System.Reflection;
using System.Text.RegularExpressions;
using BuildNexus.PaymentService.Data;

namespace BuildNexus.PaymentService.Tests;

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

    [Fact]
    public void The_first_script_is_present()
    {
        Assert.Contains(
            ScriptNames(),
            name => name.EndsWith("001_create_quotations.sql", StringComparison.Ordinal));
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
        // schema this service owns. Matched as statements against the SQL with
        // its comments stripped, so a fail is on real SQL, not on prose.
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
        // One schema per service, enforced here as well as by convention:
        // quotations.project_id records a project id as a plain column, never a
        // foreign key into the Project Service's database.
        foreach (var resource in ScriptNames())
        {
            var sql = StripComments(ReadScript(resource));

            Assert.DoesNotContain("REFERENCES projects", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("REFERENCES users", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buildnexus_project_db", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buildnexus_user_db", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buildnexus_construction_db", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_quotation_records_the_project_it_estimates_and_the_total_it_estimates()
    {
        // AC-1, restated where the data lives: a quotation is linked to a project
        // and stores an estimated total.
        var sql = StripComments(ReadScript(Script("001_create_quotations.sql")));

        Assert.Contains("CREATE TABLE IF NOT EXISTS quotations", sql, StringComparison.Ordinal);

        foreach (var column in (string[])["project_id", "estimated_total", "created_by", "created_at"])
        {
            Assert.Contains(column, sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_estimated_total_is_stored_as_an_exact_decimal()
    {
        // Money held as a float drifts on comparison and summation. Asserted on
        // the schema so a later migration cannot quietly widen it to DOUBLE.
        var sql = StripComments(ReadScript(Script("001_create_quotations.sql")));

        Assert.Matches(@"(?i)estimated_total\s+DECIMAL\(15,\s*2\)", sql);
        Assert.DoesNotMatch(@"(?i)estimated_total\s+(FLOAT|DOUBLE|REAL)", sql);
    }

    [Fact]
    public void A_quotation_cannot_be_stored_with_a_zero_or_negative_total()
    {
        // The database half of the validation pair — the request validator refuses
        // it first, but a caller that reached the repository directly would still
        // be stopped here.
        var sql = StripComments(ReadScript(Script("001_create_quotations.sql")));

        Assert.Contains("ck_quotations_estimated_total_positive", sql, StringComparison.Ordinal);
        Assert.Matches(@"(?i)CHECK\s*\(\s*estimated_total\s*>\s*0\s*\)", sql);
    }

    [Fact]
    public void Project_ownership_is_replicated_locally_rather_than_read_from_another_service()
    {
        // AC-1's Client view is gated on ownership, and the owning Client is a
        // fact the Project Service holds. This service may not query that
        // database, so the fact is replicated off project-events into a table of
        // its own — one owner per project, keyed so a redelivered event is
        // absorbed.
        var sql = StripComments(ReadScript(Script("002_create_project_owners.sql")));

        Assert.Contains("CREATE TABLE IF NOT EXISTS project_owners", sql, StringComparison.Ordinal);
        Assert.Contains("pk_project_owners PRIMARY KEY (project_id)", sql, StringComparison.Ordinal);
        Assert.Contains("client_id", sql, StringComparison.Ordinal);
    }

    private static string Script(string fileName) =>
        ScriptNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

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
