namespace BuildNexus.ProjectService.Models;

/// <summary>
/// When a project counts as stalled (US-38 AC-2): still open, and nothing has
/// touched it for <see cref="StalledAfterDays"/> days.
/// </summary>
/// <remarks>
/// Kept apart from the controller so the rule can be read, and tested, on its
/// own — and so the screen that highlights a stalled project and the count that
/// says how many there are can only ever be asking the same question.
/// <para>
/// "Touched" is <c>updated_at</c>, which moves whenever the project's status
/// changes or staff are put on it. A <see cref="ProjectStatus.Completed"/> or
/// <see cref="ProjectStatus.Cancelled"/> project is finished with, so it is never
/// stalled however long ago it moved: it is not waiting on anybody. A
/// <see cref="ProjectStatus.Pending"/> project nobody has picked up is stalled
/// like any other — that is exactly the neglect an Admin wants to see.
/// </para>
/// </remarks>
public static class ProjectStallPolicy
{
    /// <summary>
    /// How many days without movement make an open project stalled. A fortnight:
    /// long enough that a project between two ordinary hand-offs is not flagged,
    /// short enough that a forgotten one does not sit unnoticed for a month.
    /// </summary>
    public const int StalledAfterDays = 14;

    /// <summary>
    /// Whether a project in <paramref name="status"/>, last updated at
    /// <paramref name="updatedAtUtc"/>, is stalled at <paramref name="nowUtc"/>.
    /// </summary>
    /// <remarks>
    /// The moment is passed in rather than read from the clock, so a caller
    /// judging a whole list uses one instant for every row and a test can fix the
    /// day. Exactly <see cref="StalledAfterDays"/> days is stalled: the fourteenth
    /// day with no movement is the first the project is flagged on.
    /// </remarks>
    public static bool IsStalled(ProjectStatus status, DateTime updatedAtUtc, DateTime nowUtc) =>
        status is not (ProjectStatus.Completed or ProjectStatus.Cancelled)
        && nowUtc - updatedAtUtc >= TimeSpan.FromDays(StalledAfterDays);
}
