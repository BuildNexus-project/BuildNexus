namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The <c>eventType</c> values this service publishes.
/// </summary>
/// <remarks>
/// Only the events a story has actually asked for belong here. A consumer
/// subscribes by matching these strings, so an invented one is a contract
/// nobody agreed to and dead weight on the topic.
/// </remarks>
public static class ProjectEventTypes
{
    /// <summary>A Client has submitted a new project (US-05).</summary>
    public const string ProjectCreated = nameof(ProjectCreated);

    /// <summary>
    /// A project has moved to a new status (US-22). Raised on every transition,
    /// whichever one it is.
    /// </summary>
    public const string ProjectUpdated = nameof(ProjectUpdated);

    /// <summary>
    /// A project's design has been signed off — the move into
    /// <see cref="Models.ProjectStatus.DesignApproved"/> (US-22).
    /// </summary>
    /// <remarks>
    /// Raised <em>in addition to</em> <see cref="ProjectUpdated"/> rather than
    /// instead of it, and the two go out in that order. Approval is the moment
    /// the Construction Service may schedule a build and the Payment Service
    /// may raise its first invoice, and neither should have to know that
    /// "DesignApproved" is the status name that means it — a lifecycle rename
    /// here would silently stop their handlers firing. A consumer that only
    /// cares about the milestone matches this type; one that tracks the whole
    /// lifecycle reads <see cref="ProjectUpdated"/> and ignores this.
    /// </remarks>
    public const string ProjectApproved = nameof(ProjectApproved);

    /// <summary>
    /// Every type this service publishes, for code that has to enumerate them —
    /// and for the test that holds the database's own list to the same set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [ProjectCreated, ProjectUpdated, ProjectApproved];
}
