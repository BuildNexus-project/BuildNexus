namespace BuildNexus.ProjectService.Configuration;

/// <summary>
/// Settings for the outbox dispatcher, bound from the <c>Outbox</c>
/// configuration section.
/// </summary>
/// <remarks>
/// Both have working defaults, unlike the broker address: an outbox that nobody
/// configured should still drain, and these only tune how quickly.
/// </remarks>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// How long to wait before looking for pending events again, in seconds.
    /// </summary>
    /// <remarks>
    /// The delay only applies when there was nothing left to send — a pass that
    /// filled its batch goes straight round again, so a backlog drains at the
    /// broker's pace rather than one batch per interval.
    /// <para>
    /// Five seconds is the worst case a consumer waits on an idle system, which
    /// is well inside what "reacts to a project being approved" needs. Polling
    /// faster would spend queries on an empty table for latency nobody asked
    /// for.
    /// </para>
    /// </remarks>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>How many events one pass may claim.</summary>
    /// <remarks>
    /// Bounded so a long backlog is drained in steady chunks rather than read
    /// into memory all at once.
    /// </remarks>
    public int BatchSize { get; set; } = 50;
}
