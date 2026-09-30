namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Builds a <see cref="ProjectReportFilter"/> from what a request carried,
/// refusing what cannot be a filter — an unknown status, or a range that ends
/// before it starts — with a message the caller can act on.
/// </summary>
/// <remarks>
/// Kept out of the controller so the rules can be tested one at a time. An
/// endpoint that quietly ignored a misspelt status would return the whole
/// pipeline to someone who asked for one slice of it, and they would have no
/// way to tell.
/// </remarks>
public static class ProjectReportFilterParser
{
    /// <param name="statuses">
    /// Status names, each either a value of its own or several joined by commas,
    /// so <c>?status=Pending&amp;status=Designing</c> and
    /// <c>?status=Pending,Designing</c> mean the same. Case is not significant.
    /// <c>null</c>, empty, or blanks only means every status.
    /// </param>
    /// <returns><c>true</c> with <paramref name="filter"/> set, or <c>false</c> with <paramref name="error"/> set.</returns>
    public static bool TryParse(
        IEnumerable<string>? statuses,
        DateOnly? from,
        DateOnly? to,
        out ProjectReportFilter? filter,
        out string? error)
    {
        filter = null;
        error = null;

        if (from is not null && to is not null && from > to)
        {
            error = "The 'from' date must not be after the 'to' date.";
            return false;
        }

        var names = (statuses ?? [])
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var parsed = new List<ProjectStatus>();

        foreach (var name in names)
        {
            // Matched against the names rather than through Enum.TryParse, which
            // would also accept "3" and turn a number into a status.
            var match = Enum.GetNames<ProjectStatus>()
                .FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                error = $"'{name}' is not a project status. Valid statuses are: "
                    + $"{string.Join(", ", Enum.GetNames<ProjectStatus>())}.";
                return false;
            }

            var status = Enum.Parse<ProjectStatus>(match);

            if (!parsed.Contains(status))
            {
                parsed.Add(status);
            }
        }

        filter = new ProjectReportFilter
        {
            Statuses = parsed.Count == 0 ? null : parsed,
            From = from,
            To = to
        };

        return true;
    }
}
