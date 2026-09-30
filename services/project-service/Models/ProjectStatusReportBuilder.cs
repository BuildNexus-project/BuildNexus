namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Turns the rows the report query returned into a <see cref="ProjectStatusReport"/>:
/// one group per status, in lifecycle order.
/// </summary>
/// <remarks>
/// Pure, and deliberately apart from the repository: which projects are in the
/// report is the database's answer, but how they are grouped and ordered is a
/// rule of its own, and it can be checked without a database.
/// <para>
/// Every status in scope gets a group even when it is empty. An Admin reading a
/// pipeline wants to see that nothing is in <c>Designing</c>, not to wonder
/// whether the report forgot it — and a report whose columns come and go with
/// the data is harder to compare from one week to the next. "In scope" is every
/// status, or only the ones the filter named.
/// </para>
/// </remarks>
public static class ProjectStatusReportBuilder
{
    public static ProjectStatusReport Build(IEnumerable<ProjectReportRow> rows, ProjectReportFilter filter)
    {
        var inScope = filter.Statuses is null
            ? Enum.GetValues<ProjectStatus>()
            : Enum.GetValues<ProjectStatus>().Where(filter.Statuses.Contains).ToArray();

        var byStatus = rows.ToLookup(row => row.Status);

        var groups = inScope
            .Select(status => new ProjectStatusGroup
            {
                Status = status,
                // Newest submitted first, matching the order the query gives and
                // every other project list. Ordered again here rather than
                // trusting the caller's order, so the rule lives in one place.
                Projects = byStatus[status]
                    .OrderByDescending(row => row.CreatedAt)
                    .ThenBy(row => row.Id)
                    .ToList()
            })
            .ToList();

        return new ProjectStatusReport { Groups = groups };
    }
}
