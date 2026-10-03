using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The notification store held in memory, keeping the two rules the controller and consumer
/// tests lean on: one notification per (event, person), and every read and dismissal scoped to
/// the person asking.
/// </summary>
/// <remarks>
/// A fake rather than a mock, in the style of the other <c>Fake*Repository</c> classes. The SQL
/// behind these rules — the unique key, the ordering — is covered against real MySQL by
/// <c>NotificationRepositoryDatabaseTests</c>; this one exists so a test can say "given these
/// stored notifications" and read what the consumer or endpoint did with them.
/// </remarks>
public sealed class FakeNotificationRepository : INotificationRepository
{
    private readonly List<Notification> _stored = [];

    /// <summary>Everything stored, in the order it arrived.</summary>
    public IReadOnlyList<Notification> Stored => _stored;

    /// <summary>How many times <see cref="InsertAsync"/> was called, even with nothing to store.</summary>
    public int InsertCalls { get; private set; }

    /// <summary>Set to make the next <see cref="InsertAsync"/> fail, as an unreachable database would.</summary>
    public Exception? InsertThrows { get; set; }

    /// <summary>The person the last list, count or mark call was for.</summary>
    public Guid? LastUserId { get; private set; }

    /// <summary>The limit the last list call was given.</summary>
    public int? LastLimit { get; private set; }

    /// <summary>The offset the last list call was given.</summary>
    public int? LastOffset { get; private set; }

    /// <summary>The time the last mark call was given.</summary>
    public DateTime? LastReadAtUtc { get; private set; }

    public void Seed(params Notification[] notifications) => _stored.AddRange(notifications);

    public Task InsertAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken = default)
    {
        InsertCalls++;

        if (InsertThrows is not null)
        {
            throw InsertThrows;
        }

        foreach (var notification in notifications)
        {
            if (!_stored.Any(n => n.EventId == notification.EventId && n.UserId == notification.UserId))
            {
                _stored.Add(notification);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int limit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        LastUserId = userId;
        LastLimit = limit;
        LastOffset = offset;

        return Task.FromResult<IReadOnlyList<Notification>>(
            _stored.Where(n => n.UserId == userId)
                .OrderByDescending(n => n.OccurredAt)
                .ThenBy(n => n.Id)
                .Skip(offset)
                .Take(limit)
                .ToList());
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        LastUserId = userId;

        return Task.FromResult(_stored.Count(n => n.UserId == userId && !n.IsRead));
    }

    public Task<bool> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default)
    {
        LastUserId = userId;
        LastReadAtUtc = readAtUtc;

        var index = _stored.FindIndex(n => n.Id == notificationId && n.UserId == userId);

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _stored[index] = WithReadAt(_stored[index], _stored[index].ReadAt ?? readAtUtc);

        return Task.FromResult(true);
    }

    public Task<int> MarkAllReadAsync(Guid userId, DateTime readAtUtc, CancellationToken cancellationToken = default)
    {
        LastUserId = userId;
        LastReadAtUtc = readAtUtc;

        var marked = 0;

        for (var index = 0; index < _stored.Count; index++)
        {
            if (_stored[index].UserId == userId && !_stored[index].IsRead)
            {
                _stored[index] = WithReadAt(_stored[index], readAtUtc);
                marked++;
            }
        }

        return Task.FromResult(marked);
    }

    private static Notification WithReadAt(Notification n, DateTime readAt) => new()
    {
        Id = n.Id,
        UserId = n.UserId,
        ProjectId = n.ProjectId,
        EventId = n.EventId,
        EventType = n.EventType,
        Message = n.Message,
        OccurredAt = n.OccurredAt,
        ReadAt = readAt
    };
}
