using System.Reflection;
using BuildNexus.UserService.Data;

namespace BuildNexus.UserService.Tests;

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
    [InlineData("001_create_users_table.sql")]
    [InlineData("002_add_users_contact_details.sql")]
    [InlineData("003_create_password_reset_tokens_table.sql")]
    public void The_known_scripts_are_present(string fileName)
    {
        Assert.Contains(ScriptNames(), name => name.EndsWith(fileName, StringComparison.Ordinal));
    }

    [Fact]
    public void Script_names_sort_into_the_order_they_must_run_in()
    {
        // DbUp runs scripts in name order, so 002's ALTER only lands after 001
        // has created the table. Zero-padded numbers keep that true past nine.
        var names = ScriptNames();

        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal), names);
    }

    [Fact]
    public void No_script_switches_database()
    {
        // A USE or CREATE DATABASE inside a script would take effect outside the
        // schema this service owns. The connection string names the database and
        // DatabaseMigrator creates it; scripts must assume they are already in it.
        foreach (var resource in ScriptNames())
        {
            using var stream = ServiceAssembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            Assert.DoesNotContain("USE ", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CREATE DATABASE", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<string> ScriptNames() =>
        [.. ServiceAssembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)];
}
