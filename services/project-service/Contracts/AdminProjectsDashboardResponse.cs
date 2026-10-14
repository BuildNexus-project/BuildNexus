using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// The project count on an Admin's dashboard (US-21 AC-4): how many projects the
/// system holds, and how they divide across the lifecycle.
/// </summary>
/// <remarks>
/// User counts are the User Service's data and come from its own dashboard
/// endpoint; the page puts the two together.
/// </remarks>
public class AdminProjectsDashboardResponse
{
    /// <summary>Every project in the system, whatever its status — the sum of <see cref="Groups"/>.</summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// One entry per status in lifecycle order, including those nothing is in, so
    /// the shape of the answer does not change with the data.
    /// </summary>
    public IReadOnlyList<Group> Groups { get; set; } = [];

    public class Group
    {
        public string Status { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    /// <summary>
    /// Builds the answer from the tally the query returns, filling in a zero for
    /// every status the query left out.
    /// </summary>
    public static AdminProjectsDashboardResponse From(IReadOnlyList<ProjectStatusCount> counts)
    {
        var groups = Enum.GetValues<ProjectStatus>()
            .Select(status => new Group
            {
                Status = status.ToString(),
                Count = counts.Where(c => c.Status == status).Sum(c => c.Count)
            })
            .ToList();

        return new AdminProjectsDashboardResponse
        {
            TotalCount = groups.Sum(group => group.Count),
            Groups = groups
        };
    }
}
