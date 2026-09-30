using BuildNexus.ConstructionService.Data;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IPaymentSettlementRepository"/> that records the calls made to it, so
/// <see cref="Messaging.PaymentEventsConsumer"/> can be driven without MySQL. The real
/// <c>INSERT IGNORE</c> is covered by the database-backed suite.
/// </summary>
public sealed class FakePaymentSettlementRepository : IPaymentSettlementRepository
{
    /// <summary>One entry per <see cref="RecordSettlementIfAbsentAsync"/> call, in order.</summary>
    public List<SettlementCall> Calls { get; } = [];

    /// <summary>
    /// What <see cref="RecordSettlementIfAbsentAsync"/> returns. <c>false</c> stands
    /// for a project whose settlement was already recorded — the redelivery case.
    /// </summary>
    public bool RecordResult { get; set; } = true;

    /// <summary>When set, the record call throws it — a stand-in for the database being unreachable.</summary>
    public Exception? RecordThrows { get; set; }

    /// <summary>What <see cref="IsSettledAsync"/> returns.</summary>
    public bool Settled { get; set; }

    public Task<bool> RecordSettlementIfAbsentAsync(
        Guid projectId,
        Guid sourceEventId,
        DateTime settledAtUtc,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new SettlementCall(projectId, sourceEventId, settledAtUtc));

        if (RecordThrows is not null)
        {
            throw RecordThrows;
        }

        return Task.FromResult(RecordResult);
    }

    public Task<bool> IsSettledAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Settled);

    public readonly record struct SettlementCall(Guid ProjectId, Guid SourceEventId, DateTime SettledAtUtc);
}
