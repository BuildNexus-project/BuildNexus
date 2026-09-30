namespace BuildNexus.UserService.Models;

/// <summary>
/// How many accounts share one role and one active/deactivated state — one line
/// of the tally behind the Admin dashboard (US-21).
/// </summary>
/// <remarks>
/// Grouped by both so a single query answers every count the dashboard shows:
/// the total, the active and deactivated split, and the head count per role are
/// all sums of these lines. A pair nobody holds is absent rather than present
/// with a zero — the caller decides how to present an empty group.
/// </remarks>
public class UserCountGroup
{
    public UserRole Role { get; init; }

    public bool IsActive { get; init; }

    public int Count { get; init; }
}
