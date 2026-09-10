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
        // Written over the enum rather than over today's names, and across every
        // script rather than one: a status added in a later story without a
        // matching migration — wherever that migration lands — would otherwise
        // only surface as a constraint violation the first time somebody used
        // it. 001 defined Submitted; 002 added UnderReview and Approved; 003
        // added RevisionRequested.
        var sql = string.Concat(ScriptNames().Select(ReadScript));

        var missing = Enum.GetNames<Models.DesignDocumentStatus>()
            .Where(status => !sql.Contains($"'{status}'", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These statuses exist in DesignDocumentStatus but no migration allows them in the database: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void The_review_statuses_arrive_in_002()
    {
        // US-10: the two states 002 adds on top of Submitted, so the "current"
        // version can be identified.
        var sql = StripComments(ReadScript(Script("002_add_design_document_review_statuses.sql")));

        Assert.Contains("'UnderReview'", sql, StringComparison.Ordinal);
        Assert.Contains("'Approved'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_revision_requested_status_and_review_columns_arrive_in_003()
    {
        // US-11: the status a Client's revision request moves a version to, and
        // the columns that record who reviewed a version, when, and what a
        // revision request asked for.
        var sql = StripComments(ReadScript(Script("003_add_design_document_review_decision.sql")));

        Assert.Contains("'RevisionRequested'", sql, StringComparison.Ordinal);
        Assert.Matches(@"(?is)ADD COLUMN reviewed_by\b", sql);
        Assert.Matches(@"(?is)ADD COLUMN reviewed_at\b", sql);
        Assert.Matches(@"(?is)ADD COLUMN review_comment\b", sql);
    }

    [Fact]
    public void Every_event_type_the_service_can_produce_is_allowed_by_the_schema()
    {
        // Same guard as the statuses above, for the outbox event types: an
        // eventType added to DesignEventTypes without a matching migration
        // would only surface as a ck_design_outbox_events_type violation the
        // first time it was raised. 004 allowed DesignApproved; 005 added
        // DesignSubmitted and DesignRevisionRequested.
        var sql = string.Concat(ScriptNames().Select(ReadScript));

        var missing = Messaging.DesignEventTypes.All
            .Where(eventType => !sql.Contains($"'{eventType}'", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These event types exist in DesignEventTypes but no migration allows them in the database: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void The_extra_event_types_arrive_in_005()
    {
        // US-23: the two events design-service now raises alongside DesignApproved.
        var sql = StripComments(ReadScript(Script("005_add_design_outbox_event_types.sql")));

        Assert.Contains("'DesignSubmitted'", sql, StringComparison.Ordinal);
        Assert.Contains("'DesignRevisionRequested'", sql, StringComparison.Ordinal);
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
