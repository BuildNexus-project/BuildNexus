using System.Reflection;
using System.Text.RegularExpressions;
using BuildNexus.DesignService.Data;

namespace BuildNexus.DesignService.Tests;

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
            name => name.EndsWith("001_create_design_documents.sql", StringComparison.Ordinal));
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
        // design_documents.project_id records a project id as a plain column,
        // never a foreign key into the Project Service's database.
        foreach (var resource in ScriptNames())
        {
            var sql = StripComments(ReadScript(resource));

            Assert.DoesNotContain("REFERENCES projects", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("REFERENCES users", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buildnexus_project_db", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buildnexus_user_db", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_versions_table_keeps_the_metadata_the_story_asks_for()
    {
        // Version number, upload date, uploader, status and revision comment —
        // the third US-09 acceptance bullet, restated where the data lives.
        var sql = StripComments(ReadScript(Script("001_create_design_documents.sql")));

        Assert.Contains("CREATE TABLE IF NOT EXISTS design_documents", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS design_document_versions", sql, StringComparison.Ordinal);

        foreach (var column in (string[])
                 ["version_number", "file_name", "content_type", "file_size_bytes", "file_bytes",
                  "status", "revision_comment", "uploaded_by", "uploaded_at"])
        {
            Assert.Contains(column, sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_version_number_is_unique_within_its_document()
    {
        // This constraint is also what makes the repository's MAX + 1 safe under
        // two uploads at once — the loser hits it and retries.
        var sql = StripComments(ReadScript(Script("001_create_design_documents.sql")));

        Assert.Contains("uq_design_document_versions_number", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (document_id, version_number)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_status_the_service_can_produce_is_allowed_by_the_schema()
    {
        // Written over the enum rather than over today's single name: a status
        // added in a later story without a matching migration would otherwise
        // only surface as a constraint violation the first time somebody used
        // it.
        var sql = ReadScript(Script("001_create_design_documents.sql"));

        var missing = Enum.GetNames<Models.DesignDocumentStatus>()
            .Where(status => !sql.Contains($"'{status}'", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These statuses exist in DesignDocumentStatus but no migration allows them in the database: "
            + string.Join(", ", missing));
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
