namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// The <c>eventType</c> values this service publishes.
/// </summary>
/// <remarks>
/// Only the events a story has actually asked for belong here. A consumer
/// subscribes by matching these strings, so an invented one is a contract
/// nobody agreed to and dead weight on the topic.
/// </remarks>
public static class DesignEventTypes
{
    /// <summary>An Architect has uploaded a design document version (US-23).</summary>
    public const string DesignSubmitted = nameof(DesignSubmitted);

    /// <summary>A Client has asked for changes on a design document version (US-23).</summary>
    public const string DesignRevisionRequested = nameof(DesignRevisionRequested);

    /// <summary>A Client has approved a design document version (US-11).</summary>
    public const string DesignApproved = nameof(DesignApproved);

    /// <summary>
    /// Every type this service publishes, for code that has to enumerate them —
    /// and for the test that holds the database's own list to the same set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [DesignSubmitted, DesignRevisionRequested, DesignApproved];
}
