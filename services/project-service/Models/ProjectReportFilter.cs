namespace BuildNexus.ProjectService.Models;

/// <summary>
/// The narrowing an Admin can apply to the project status report (US-18): by
/// status, by the date a project was submitted, or both.
/// </summary>
/// <remarks>
/// The date range is on <c>created_at</c>, the day the Client submitted the
/// project. It is the one date every project has and the one that never moves;
/// <c>updated_at</c> changes with every status move, so a range over it would
/// answer "what was touched" rather than "what came in".
/// <para>
/// <see cref="From"/> and <see cref="To"/> are whole days, both inclusive, so a
/// report for one day is <c>From == To</c>. Every property is optional: an empty
/// filter is the whole pipeline.
/// </para>
/// </remarks>
public class ProjectReportFilter
{
    /// <summary>
    /// Only these statuses, or <c>null</c> for all of them. An empty set is not
    /// the same as <c>null</c> — it matches nothing.
    /// </summary>
    public IReadOnlyCollection<ProjectStatus>? Statuses { get; init; }

    /// <summary>First day to include, or <c>null</c> for no lower bound.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last day to include, or <c>null</c> for no upper bound.</summary>
    public DateOnly? To { get; init; }

    /// <summary>The start of <see cref="From"/>, for a <c>created_at &gt;=</c> comparison.</summary>
    public DateTime? FromInclusiveUtc => From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// The start of the day <em>after</em> <see cref="To"/>, for a
    /// <c>created_at &lt;</c> comparison. Exclusive so that a project created at
    /// 23:59:59 on the last day is in, without needing to know how finely the
    /// column stores time.
    /// </summary>
    public DateTime? ToExclusiveUtc => To?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
