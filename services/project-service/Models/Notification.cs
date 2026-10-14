namespace BuildNexus.ProjectService.Models;

/// <summary>
/// One thing a person has been told about (US-26), mapped by hand from the
/// <c>notifications</c> table.
/// </summary>
/// <remarks>
/// One row per person per event: an event that concerns a project's Client and its Architect
/// becomes two of these, each read and dismissed on its own.
/// </remarks>
public class Notification
{
    public Guid Id { get; init; }

    /// <summary>
    /// Who it is for, as the User Service knows them. Not a foreign key — the account lives in
    /// that service's database.
    /// </summary>
    public Guid UserId { get; init; }

    public Guid ProjectId { get; init; }

    /// <summary>
    /// The <c>eventId</c> of the event this came from. With <see cref="UserId"/> it is the
    /// natural key: the same event arriving twice must not tell the same person twice.
    /// </summary>
    public Guid EventId { get; init; }

    /// <summary>One of <see cref="NotificationEventTypes"/>.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>The finished sentence the person reads.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// When the event happened, in UTC — taken off its envelope, not the moment it was read.
    /// </summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>When the person marked it read, in UTC; <c>null</c> while they have not.</summary>
    public DateTime? ReadAt { get; init; }

    public bool IsRead => ReadAt is not null;
}
