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
}
