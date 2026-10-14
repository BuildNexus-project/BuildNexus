using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// The user counts on an Admin's dashboard (US-21 AC-4): how many accounts the
/// system holds, how many of them can sign in, and how they divide by role.
/// </summary>
/// <remarks>
/// Project counts are the Project Service's data and come from its own
/// dashboard endpoint; the page puts the two together. Deactivated accounts are
/// counted in the total — they are still accounts an Admin administers — and
/// reported separately so the two numbers can be told apart.
/// </remarks>
public class AdminUsersDashboardResponse
{
    /// <summary>Every account, active or not — the sum of <see cref="Roles"/>.</summary>
    public int TotalUsers { get; set; }

    /// <summary>Accounts that can sign in.</summary>
    public int ActiveUsers { get; set; }

    /// <summary>Accounts an Admin has withdrawn access from.</summary>
    public int InactiveUsers { get; set; }

    /// <summary>
    /// One entry per platform role in the order the platform lists them,
    /// including roles nobody holds, so the shape does not change with the data.
    /// </summary>
    public IReadOnlyList<RoleCount> Roles { get; set; } = [];

    public class RoleCount
    {
        public string Role { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    /// <summary>
    /// Builds the answer from the tally the query returns, filling in a zero for
    /// every role the query left out.
    /// </summary>
    public static AdminUsersDashboardResponse From(IReadOnlyList<UserCountGroup> groups)
    {
        var total = groups.Sum(group => group.Count);
        var active = groups.Where(group => group.IsActive).Sum(group => group.Count);

        return new AdminUsersDashboardResponse
        {
            TotalUsers = total,
            ActiveUsers = active,
            InactiveUsers = total - active,
            Roles = Enum.GetValues<UserRole>()
                .Select(role => new RoleCount
                {
                    Role = role.ToString(),
                    Count = groups.Where(group => group.Role == role).Sum(group => group.Count)
                })
                .ToList()
        };
    }
}
