using System.Data.Common;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// ADO.NET access to the <c>notifications</c> table (US-26). Direct SQL only — no ORM. Every
/// value reaches MySQL as a bound parameter.
/// </summary>
public class NotificationRepository : INotificationRepository
{
    private const string Columns = "id, user_id, project_id, event_id, event_type, message, occurred_at, read_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(
        IReadOnlyList<Notification> notifications,
        CancellationToken cancellationToken = default)
    {
        if (notifications.Count == 0)
        {
            return;
        }

        // `ON DUPLICATE KEY UPDATE id = id` rather than INSERT IGNORE. Both skip a row whose
        // (event_id, user_id) is already there — the redelivery case — but IGNORE would also
        // downgrade a foreign-key failure to a warning, and a notification for a project that
        // does not exist has to fail loudly rather than vanish.
        const string sql = @"
            INSERT INTO notifications
                (id, user_id, project_id, event_id, event_type, message, occurred_at)
            VALUES
                (@id, @userId, @projectId, @eventId, @eventType, @message, @occurredAt)
            ON DUPLICATE KEY UPDATE id = id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            AddParameter(command, "@id", notification.Id);
            AddParameter(command, "@userId", notification.UserId);
            AddParameter(command, "@projectId", notification.ProjectId);
            AddParameter(command, "@eventId", notification.EventId);
            AddParameter(command, "@eventType", notification.EventType);
            AddParameter(command, "@message", notification.Message);
            AddParameter(command, "@occurredAt", notification.OccurredAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int limit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        // `id` breaks the tie between notifications that happened in the same instant, so the
        // order is the same on every read — which is what makes a second page start exactly
        // where the first left off.
        var sql = $@"
            SELECT {Columns}
            FROM notifications
            WHERE user_id = @userId
            ORDER BY occurred_at DESC, id
            LIMIT @limit OFFSET @offset;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@userId", userId);
        AddParameter(command, "@limit", limit);
        AddParameter(command, "@offset", offset);

        var notifications = new List<Notification>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            notifications.Add(Map(reader));
        }

        return notifications;
    }

    public async Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM notifications
            WHERE user_id = @userId
              AND read_at IS NULL;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@userId", userId);

        // COUNT(*) is a BIGINT; the value is a tally of notifications, not a key.
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default)
    {
        // COALESCE keeps the time it was FIRST read: reading it again must not move it. Scoped by
        // user_id in the same statement, so "not yours" and "does not exist" are one answer.
        // MySqlConnector counts rows matched rather than rows changed, so an already-read
        // notification still answers 1 — a repeat is a success, not a miss.
        const string sql = @"
            UPDATE notifications
            SET read_at = COALESCE(read_at, @readAt)
            WHERE id = @id
              AND user_id = @userId;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@readAt", readAtUtc);
        AddParameter(command, "@id", notificationId);
        AddParameter(command, "@userId", userId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> MarkAllReadAsync(
        Guid userId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE notifications
            SET read_at = @readAt
            WHERE user_id = @userId
              AND read_at IS NULL;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@readAt", readAtUtc);
        AddParameter(command, "@userId", userId);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Notification Map(DbDataReader reader) => new()
    {
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        EventId = reader.GetGuid(reader.GetOrdinal("event_id")),
        EventType = reader.GetString(reader.GetOrdinal("event_type")),
        Message = reader.GetString(reader.GetOrdinal("message")),
        OccurredAt = AsUtc(reader.GetDateTime(reader.GetOrdinal("occurred_at"))),
        ReadAt = GetNullableUtc(reader, "read_at")
    };

    /// <summary>
    /// A DATETIME comes back <see cref="DateTimeKind.Unspecified"/>. Everything written here is
    /// UTC, so it is relabelled rather than converted — otherwise it would serialise without a
    /// <c>Z</c> and a browser would read it as the viewer's own local time.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? GetNullableUtc(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal) ? null : AsUtc(reader.GetDateTime(ordinal));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
