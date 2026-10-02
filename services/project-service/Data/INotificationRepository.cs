using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// Where each person's notifications are kept (US-26): written by the event consumer, read and
/// dismissed by the person they are for.
/// </summary>
/// <remarks>
/// Separate from <see cref="IProjectRepository"/> for the same reason the dashboard and report
/// queries are: this is its own table with its own readers and writers, and a handler that
/// moves a project has no business being able to reach it.
/// <para>
/// Every read and every dismissal is scoped by the person's id. There is no way to ask for
/// "notification 42" without saying whose it is, so one person cannot read or dismiss another's
/// by guessing an id.
/// </para>
/// </remarks>
public interface INotificationRepository
{
    /// <summary>
    /// Stores the given notifications in one transaction — all of them or none.
    /// </summary>
    /// <remarks>
    /// Idempotent on (event id, user): a notification whose event has already told that person
    /// is skipped silently, not refused. Kafka delivers at least once, so the same event arriving
    /// again is the ordinary case, and it must not tell anyone twice.
    /// </remarks>
    Task InsertAsync(
        IReadOnlyList<Notification> notifications,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The person's most recent notifications, newest first, at most <paramref name="limit"/>.
    /// Read and unread alike.
    /// </summary>
    Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many notifications the person has not yet read — all of them, not just the ones
    /// <see cref="ListForUserAsync"/> would return.
    /// </summary>
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks one of the person's notifications read. Answers <c>true</c> when it exists and is
    /// theirs — including when it was already read, which is not an error — and <c>false</c>
    /// when there is no such notification for them.
    /// </summary>
    /// <remarks>
    /// A notification that belongs to somebody else answers the same as one that does not exist,
    /// so the answer never confirms that another person's id is real.
    /// </remarks>
    Task<bool> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every unread notification of the person's read, and says how many that was.
    /// </summary>
    Task<int> MarkAllReadAsync(
        Guid userId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default);
}
