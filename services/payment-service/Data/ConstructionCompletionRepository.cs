using System.Data;
using System.Data.Common;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// ADO.NET data access for <c>construction_completions</c>. Direct SQL only —
/// no ORM. Every value reaches MySQL as a bound parameter.
/// </summary>
public class ConstructionCompletionRepository : IConstructionCompletionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ConstructionCompletionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> RecordCompletionAndMaybeAnnounceAsync(
        Guid projectId,
        DateTime completedAtUtc,
        Guid sourceEventId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // INSERT IGNORE: project_id is the primary key, so the one thing this can
        // silently skip is a build already recorded complete — exactly the
        // idempotency a redelivered ConstructionCompleted needs. It does not
        // overwrite, so the first completion learned of wins, and announced_at
        // on an existing row is left alone rather than reset.
        const string sql = @"
            INSERT IGNORE INTO construction_completions
                (project_id, completed_at, source_event_id, recorded_at)
            VALUES
                (@projectId, @completedAt, @sourceEventId, @recordedAt);";

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            AddParameter(command, "@projectId", projectId);
            AddParameter(command, "@completedAt", completedAtUtc);
            AddParameter(command, "@sourceEventId", sourceEventId);
            AddParameter(command, "@recordedAt", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Evaluated whether or not this call inserted the row: a redelivery that
        // inserts nothing must still be able to announce, because the first
        // delivery may have arrived while the project still owed money.
        var announced = await FinalSettlementAnnouncer.TryAnnounceAsync(
            connection, transaction, projectId, completedAtUtc, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return announced;
    }

    public Task<bool> IsCompleteAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        ExistsAsync(
            "SELECT EXISTS (SELECT 1 FROM construction_completions WHERE project_id = @projectId);",
            projectId,
            cancellationToken);

    public Task<bool> HasAnnouncedAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        ExistsAsync(
            @"SELECT EXISTS (
                  SELECT 1 FROM construction_completions
                  WHERE project_id = @projectId AND announced_at IS NOT NULL
              );",
            projectId,
            cancellationToken);

    private async Task<bool> ExistsAsync(string sql, Guid projectId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        // MySQL's EXISTS yields 1 or 0 as a signed integer; Convert handles the
        // width MySqlConnector picks without pinning this to one CLR type.
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is not null && Convert.ToInt64(result) == 1;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
