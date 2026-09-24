using System.Data.Common;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// ADO.NET data access for <c>payment_settlements</c>. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class PaymentSettlementRepository : IPaymentSettlementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PaymentSettlementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> RecordSettlementIfAbsentAsync(
        Guid projectId,
        Guid sourceEventId,
        DateTime settledAtUtc,
        CancellationToken cancellationToken = default)
    {
        // INSERT IGNORE: project_id is the primary key, so the one thing this can
        // silently skip is a project whose settlement is already recorded — exactly
        // the idempotency a redelivered event needs. It does not overwrite, so the
        // first settlement learned of wins.
        const string sql = @"
            INSERT IGNORE INTO payment_settlements
                (project_id, source_event_id, settled_at, recorded_at)
            VALUES
                (@projectId, @sourceEventId, @settledAt, @recordedAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);
        AddParameter(command, "@sourceEventId", sourceEventId);
        AddParameter(command, "@settledAt", settledAtUtc);
        AddParameter(command, "@recordedAt", DateTime.UtcNow);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> IsSettledAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT EXISTS (
                SELECT 1 FROM payment_settlements WHERE project_id = @projectId
            );";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        // MySQL's EXISTS yields 1 or 0 as a signed integer; Convert handles the width
        // MySqlConnector picks without pinning this to one CLR type.
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
