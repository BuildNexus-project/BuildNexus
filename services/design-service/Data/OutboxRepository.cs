using System.Data.Common;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Data;

/// <summary>
/// ADO.NET data access for the <c>design_outbox_events</c> table. Direct SQL
/// only — no ORM. Every value reaches MySQL as a bound parameter.
/// </summary>
public class OutboxRepository : IOutboxRepository
{
    private const string SelectColumns =
        "sequence_number, id, document_id, event_type, envelope, occurred_at, published_at, "
        + "attempt_count, last_error";

    /// <summary>
    /// What <c>last_error</c> holds. TEXT reaches 65,535 bytes, and this stays
    /// well inside that even once a multi-byte character is counted as more
    /// than one.
    /// </summary>
    private const int MaxErrorLength = 2000;

    private readonly IDbConnectionFactory _connectionFactory;

    public OutboxRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM design_outbox_events
            WHERE published_at IS NULL
            ORDER BY sequence_number
            LIMIT @batchSize;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@batchSize", batchSize);

        return await ReadListAsync(command, cancellationToken);
    }

    public async Task MarkPublishedAsync(
        Guid eventId,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // last_error is cleared: it described an attempt that has since been
        // superseded, and a delivered event still carrying an error reads as a
        // problem when there is none.
        const string sql = @"
            UPDATE design_outbox_events
            SET published_at  = @publishedAt,
                attempt_count = attempt_count + 1,
                last_error    = NULL
            WHERE id = @id
              AND published_at IS NULL;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@publishedAt", publishedAtUtc);
        AddParameter(command, "@id", eventId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid eventId,
        string error,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE design_outbox_events
            SET attempt_count = attempt_count + 1,
                last_error    = @error
            WHERE id = @id
              AND published_at IS NULL;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@error", Truncate(error, MaxErrorLength));
        AddParameter(command, "@id", eventId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxEvent>> ListForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            SELECT {SelectColumns}
            FROM design_outbox_events
            WHERE document_id = @documentId
            ORDER BY sequence_number;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@documentId", documentId);

        return await ReadListAsync(command, cancellationToken);
    }

    /// <summary>
    /// Appends one outbox row on an existing connection and transaction, so it
    /// commits with the state change it announces or not at all.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="IOutboxRepository"/>: it is only ever
    /// correct to call this from inside somebody else's transaction, and an
    /// interface method that enqueued an event on its own connection would be
    /// an event that could commit while the change it describes rolled back.
    /// </remarks>
    internal static async Task InsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        OutboxEvent outboxEvent)
    {
        const string sql = @"
            INSERT INTO design_outbox_events
                (id, document_id, event_type, envelope, occurred_at)
            VALUES
                (@id, @documentId, @eventType, @envelope, @occurredAt);";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@id", outboxEvent.Id);
        AddParameter(command, "@documentId", outboxEvent.DocumentId);
        AddParameter(command, "@eventType", outboxEvent.EventType);
        AddParameter(command, "@envelope", outboxEvent.Envelope);
        AddParameter(command, "@occurredAt", outboxEvent.OccurredAt);

        await command.ExecuteNonQueryAsync();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static async Task<IReadOnlyList<OutboxEvent>> ReadListAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var events = new List<OutboxEvent>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(Map(reader));
        }

        return events;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // An event that has never failed holds a real NULL in last_error, not
        // an empty string.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static DateTime? GetNullableDateTime(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static string? GetNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static OutboxEvent Map(DbDataReader reader) => new()
    {
        SequenceNumber = reader.GetInt64(reader.GetOrdinal("sequence_number")),
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("document_id")),
        EventType = reader.GetString(reader.GetOrdinal("event_type")),
        Envelope = reader.GetString(reader.GetOrdinal("envelope")),
        OccurredAt = reader.GetDateTime(reader.GetOrdinal("occurred_at")),
        PublishedAt = GetNullableDateTime(reader, "published_at"),
        AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
        LastError = GetNullableString(reader, "last_error")
    };
}
