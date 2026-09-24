namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// The <c>eventType</c> values this service publishes.
/// </summary>
/// <remarks>
/// Only the events US-14 actually names belong here. A consumer subscribes by
/// matching these strings, so an invented one is a contract nobody agreed to and
/// dead weight on the topic — and <c>ck_construction_outbox_events_type</c> holds
/// the database to the same list.
/// </remarks>
public static class ConstructionEventTypes
{
    /// <summary>
    /// A Project Manager has formally started the build (AC-1). The Project
    /// Service's cue to move the project to <c>Construction</c>.
    /// </summary>
    public const string ConstructionStarted = nameof(ConstructionStarted);

    /// <summary>
    /// Every milestone is done and the Project Manager has marked the build
    /// complete (AC-2).
    /// </summary>
    public const string ConstructionCompleted = nameof(ConstructionCompleted);

    /// <summary>
    /// Every type this service publishes, for code that has to enumerate them —
    /// and for the test that holds the database's own CHECK constraint to the
    /// same set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [ConstructionStarted, ConstructionCompleted];
}
