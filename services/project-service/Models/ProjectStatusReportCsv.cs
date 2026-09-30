using System.Globalization;
using System.Text;

namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Writes a <see cref="ProjectStatusReport"/> as CSV, for the export (US-18
/// AC-2): one row per project, grouped by status in lifecycle order, opening in
/// a spreadsheet as it is.
/// </summary>
/// <remarks>
/// Pure, so the format can be checked without a request. Two things here are
/// easy to get wrong and are worth the care:
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Quoting.</b> A project name is whatever a Client typed, commas and quotes
/// included. A field that contains one is wrapped in quotes with the quotes
/// inside doubled (RFC 4180), or the row would split into the wrong columns.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Formulas.</b> An Admin opens this in Excel, and a name such as
/// <c>=HYPERLINK(...)</c> would run there. Text that starts with a character a
/// spreadsheet reads as a formula is prefixed with an apostrophe, which it
/// shows as plain text.
/// </description>
/// </item>
/// </list>
/// Dates are written the same way for everyone — UTC, year first — and the
/// budget with two decimals and no thousands separator, so neither depends on
/// the server's culture.
/// </remarks>
public static class ProjectStatusReportCsv
{
    public static readonly IReadOnlyList<string> Header =
    [
        "Status", "Project ID", "Name", "Location", "Budget", "Created (UTC)", "Last updated (UTC)"
    ];

    private const string LineEnd = "\r\n";

    public static string Write(ProjectStatusReport report)
    {
        var csv = new StringBuilder();

        AppendLine(csv, Header);

        foreach (var group in report.Groups)
        {
            foreach (var project in group.Projects)
            {
                AppendLine(csv,
                [
                    group.Status.ToString(),
                    project.Id.ToString(),
                    project.Name,
                    project.Location,
                    project.Budget.ToString("F2", CultureInfo.InvariantCulture),
                    FormatUtc(project.CreatedAt),
                    FormatUtc(project.UpdatedAt)
                ]);
            }
        }

        return csv.ToString();
    }

    private static void AppendLine(StringBuilder csv, IEnumerable<string> fields)
    {
        csv.Append(string.Join(',', fields.Select(Escape))).Append(LineEnd);
    }

    private static string Escape(string field)
    {
        // A spreadsheet treats a cell starting with one of these as a formula.
        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            field = "'" + field;
        }

        return field.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
    }

    private static string FormatUtc(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
