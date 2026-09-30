namespace BuildNexus.ConstructionService.Data;

/// <summary>Data access for <c>payment_settlements</c>.</summary>
/// <remarks>
/// The replica of one fact the Payment Service owns — that a project's final payment
/// has landed — kept locally so AC-4's handover gate can be answered inside the
/// transaction that writes, without querying another service's database or holding a
/// foreign key into it.
/// </remarks>
public interface IPaymentSettlementRepository
{
    /// <summary>
    /// Records that a project's final payment has settled, unless it is already
    /// recorded.
    /// </summary>
    /// <remarks>
    /// Idempotent, deliberately: Kafka delivers at least once, so a redelivered
    /// <c>FinalPaymentSettled</c> must be absorbed rather than becoming an error that
    /// wedges the consumer's partition. The <c>project_id</c> primary key does the
    /// absorbing, and it does not overwrite — the first settlement this service
    /// learned of wins, which is right because a final payment settles once.
    /// Returns <c>true</c> only when a row was actually written.
    /// </remarks>
    Task<bool> RecordSettlementIfAbsentAsync(
        Guid projectId,
        Guid sourceEventId,
        DateTime settledAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this project's final payment is recorded as settled.
    /// </summary>
    /// <remarks>
    /// <c>false</c> covers both "not settled" and "this service has not learned of it
    /// yet" — a project whose <c>FinalPaymentSettled</c> was published before this
    /// service started consuming <c>payment-events</c>. Both answers refuse handover,
    /// which is the safe direction: a project held back can be handed over once the
    /// marker arrives, whereas one handed over unpaid cannot be un-handed.
    /// </remarks>
    Task<bool> IsSettledAsync(Guid projectId, CancellationToken cancellationToken = default);
}
