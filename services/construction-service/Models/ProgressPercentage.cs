namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The one definition of "how far along" a build is: milestones completed as a
/// percentage of the milestones planned.
/// </summary>
/// <remarks>
/// Shared by every read that reports progress — a project's own progress screen
/// (US-12 AC-3), the construction report (US-19) and the role dashboards (US-21) — so a
/// project's figure is the same number wherever it is shown. It is computed here, in C#,
/// and never in SQL: a second definition in a query could round differently, and two
/// screens quoting different percentages for one build is the kind of disagreement nobody
/// trusts either figure after.
/// </remarks>
public static class ProgressPercentage
{
    /// <summary>
    /// <paramref name="completed"/> out of <paramref name="total"/> as a percentage, rounded
    /// to two decimal places.
    /// </summary>
    /// <remarks>
    /// Zero when there are no milestones — a project that is approved but has nothing
    /// planned, or a started build somehow left with none, is a real state, and dividing by
    /// zero would make it a failure every caller had to guard against. Rounding is
    /// <see cref="Math.Round(decimal, int)"/>'s default, half to even, exactly as each
    /// caller had it before this was shared.
    /// </remarks>
    public static decimal Of(int completed, int total) =>
        total == 0 ? 0m : Math.Round((decimal)completed / total * 100m, 2);
}
