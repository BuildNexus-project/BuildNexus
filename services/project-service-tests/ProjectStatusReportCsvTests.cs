using System.Globalization;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The CSV the export produces (US-18 AC-2): the columns, the order, and the
/// quoting a project name typed by a Client can put to the test. No database.
/// </summary>
public class ProjectStatusReportCsvTests
{
    private static readonly DateTime Created = new(2026, 9, 1, 9, 5, 7, DateTimeKind.Utc);

    private static readonly ProjectReportFilter NoFilter = new();

    [Fact]
    public void Starts_with_a_header_row_naming_the_columns()
    {
        var lines = Lines(ProjectStatusReportBuilder.Build([], NoFilter));

        Assert.Equal(
            "Status,Project ID,Name,Location,Budget,Created (UTC),Last updated (UTC)",
            Assert.Single(lines));
    }

    [Fact]
    public void Writes_one_row_per_project_with_every_column()
    {
        var id = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");
        var report = ProjectStatusReportBuilder.Build(
            [Row(ProjectStatus.Designing, id: id, name: "Beachfront villa", budget: 18_500_000m)], NoFilter);

        var lines = Lines(report);

        Assert.Equal(2, lines.Count);
        Assert.Equal(
            $"Designing,{id},Beachfront villa,Galle,18500000.00,2026-09-01 09:05:07,2026-09-02 10:00:00",
            lines[1]);
    }

    [Fact]
    public void Rows_follow_the_lifecycle_order_of_the_groups()
    {
        var report = ProjectStatusReportBuilder.Build(
        [
            Row(ProjectStatus.Completed),
            Row(ProjectStatus.Pending),
            Row(ProjectStatus.Construction)
        ], NoFilter);

        var statuses = Lines(report).Skip(1).Select(line => line.Split(',')[0]);

        Assert.Equal(["Pending", "Construction", "Completed"], statuses);
    }

    [Fact]
    public void A_project_name_with_a_comma_or_a_quote_stays_in_one_column()
    {
        var report = ProjectStatusReportBuilder.Build(
            [Row(ProjectStatus.Pending, name: "Villa, \"Sea View\" phase 2")], NoFilter);

        Assert.Contains(",\"Villa, \"\"Sea View\"\" phase 2\",", Lines(report)[1]);
    }

    [Fact]
    public void A_name_with_a_line_break_is_quoted_not_split_into_two_rows()
    {
        var report = ProjectStatusReportBuilder.Build(
            [Row(ProjectStatus.Pending, name: "Line one\nLine two")], NoFilter);

        var csv = ProjectStatusReportCsv.Write(report);

        Assert.Contains("\"Line one\nLine two\"", csv);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Text_a_spreadsheet_would_run_as_a_formula_is_written_as_plain_text(string name)
    {
        var report = ProjectStatusReportBuilder.Build([Row(ProjectStatus.Pending, name: name)], NoFilter);

        var csv = ProjectStatusReportCsv.Write(report);

        // Whatever quoting the field needed, its content now opens with an
        // apostrophe rather than the formula character.
        Assert.DoesNotContain($",{name},", csv);
        Assert.Contains("'" + name.Replace("\"", "\"\""), csv);
    }

    [Fact]
    public void The_budget_is_culture_independent_with_two_decimals()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var report = ProjectStatusReportBuilder.Build(
                [Row(ProjectStatus.Pending, budget: 1_234_567.5m)], NoFilter);

            Assert.Contains(",1234567.50,", Lines(report)[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Lines_end_with_a_carriage_return_and_line_feed()
    {
        var csv = ProjectStatusReportCsv.Write(
            ProjectStatusReportBuilder.Build([Row(ProjectStatus.Pending)], NoFilter));

        Assert.EndsWith("\r\n", csv);
        Assert.Equal(2, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static List<string> Lines(ProjectStatusReport report) =>
        ProjectStatusReportCsv.Write(report).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

    private static ProjectReportRow Row(
        ProjectStatus status,
        Guid? id = null,
        string name = "Test project",
        decimal budget = 1_000_000m) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Location = "Galle",
        Status = status,
        Budget = budget,
        CreatedAt = Created,
        UpdatedAt = Created.AddDays(1).AddMinutes(54).AddSeconds(53)
    };
}
