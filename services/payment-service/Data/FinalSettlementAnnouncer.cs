using System.Data.Common;
using BuildNexus.PaymentService.Messaging;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Decides whether a project's final payment has landed, and enqueues
/// <c>FinalPaymentSettled</c> exactly once if it has.
/// </summary>
/// <remarks>
/// Shared by the two places that can make it true, because either can be the
/// last to arrive: a payment that settles the project's remaining balance, and
/// a <c>ConstructionCompleted</c> for a project that is already paid up. Both
/// call this inside their own transaction, so the check, the enqueue and the
/// marker commit together with whatever caused them.
/// <para>
/// Not on <see cref="IConstructionCompletionRepository"/> and not an injected
/// service: like <see cref="OutboxRepository.InsertAsync"/>, it is only ever
/// correct to call this from inside somebody else's transaction, and an
/// interface method that opened its own connection would be an announcement
/// that could commit while the payment it followed rolled back.
/// </para>
/// </remarks>
internal static class FinalSettlementAnnouncer
{
    /// <summary>
    /// Announces the project's final settlement if both conditions now hold and
    /// it has not been announced before.
    /// </summary>
    /// <remarks>
    /// The two conditions, and why both:
    /// <list type="bullet">
    /// <item>
    /// The build is complete, so no further invoices are expected. Without this,
    /// a Client who fully paid the invoice raised at construction start would be
    /// announced as settled while most of the project was still unbilled — and
    /// the Construction Service would allow handover on it.
    /// </item>
    /// <item>
    /// Nothing is outstanding, computed the same way US-16's cap computes it, so
    /// the figure that gates handover and the figure the pay endpoint enforces
    /// can never disagree.
    /// </item>
    /// </list>
    /// <para>
    /// <c>announced_at</c> is read under <c>FOR UPDATE</c> and written in the
    /// same transaction, so two triggers racing — a final payment landing just
    /// as the completion event is consumed — cannot both announce.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> when this call enqueued the event.</returns>
    internal static async Task<bool> TryAnnounceAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        DateTime settledAtUtc,
        CancellationToken cancellationToken)
    {
        if (!await IsCompleteAndUnannouncedAsync(connection, transaction, projectId, cancellationToken))
        {
            return false;
        }

        if (await OutstandingBalanceAsync(connection, transaction, projectId, cancellationToken) > 0m)
        {
            return false;
        }

        await OutboxRepository.InsertAsync(
            connection,
            transaction,
            PaymentEvents.FinalSettled(projectId, settledAtUtc),
            cancellationToken);

        await MarkAnnouncedAsync(connection, transaction, projectId, settledAtUtc, cancellationToken);

        return true;
    }

    /// <summary>
    /// Whether the project's build is recorded complete and nothing has been
    /// announced for it yet, holding the row for the rest of the transaction.
    /// </summary>
    private static async Task<bool> IsCompleteAndUnannouncedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        // FOR UPDATE is what makes the announcement exactly-once under a race:
        // the second trigger waits for the first to commit and then reads the
        // announced_at it wrote.
        const string sql = @"
            SELECT announced_at
            FROM construction_completions
            WHERE project_id = @projectId
            FOR UPDATE;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // No row means the build is not complete, so nothing is final yet.
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        return reader.IsDBNull(reader.GetOrdinal("announced_at"));
    }

    /// <summary>
    /// What the project still owes across every invoice raised against it.
    /// </summary>
    /// <remarks>
    /// The same arithmetic as US-16's per-invoice cap and US-17's balance, rolled
    /// up to the project: total billed less total paid. A project with no
    /// invoices owes nothing, which is the honest answer — blocking handover on a
    /// project nobody billed would block it forever.
    /// </remarks>
    private static async Task<decimal> OutstandingBalanceAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                COALESCE((
                    SELECT SUM(amount) FROM invoices WHERE project_id = @projectId
                ), 0)
                -
                COALESCE((
                    SELECT SUM(p.amount)
                    FROM payments p
                    INNER JOIN invoices i ON i.id = p.invoice_id
                    WHERE i.project_id = @projectId
                ), 0);";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@projectId", projectId);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    private static async Task MarkAnnouncedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid projectId,
        DateTime announcedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE construction_completions
            SET announced_at = @announcedAt
            WHERE project_id = @projectId
              AND announced_at IS NULL;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@announcedAt", announcedAtUtc);
        AddParameter(command, "@projectId", projectId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
