using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Data access for the <c>construction_outbox_events</c> table, from the dispatch
/// side.
/// </summary>
/// <remarks>
/// There is deliberately no insert here. An outbox row is only ever written in the
/// same transaction as the transition it describes, so it is written by
/// <see cref="ConstructionPhaseRepository"/> alongside the phase — an event that
/// could be enqueued on its own would be an event that might commit while the
/// transition it announces rolled back.
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>
    /// The events still waiting to be sent, oldest first, up to
    /// <paramref name="batchSize"/>.
    /// </summary>
    /// <remarks>
    /// In sequence order, so events about one project reach the topic in the order
    /// they were raised — a consumer that saw <c>ConstructionCompleted</c> before
    /// the <c>ConstructionStarted</c> for the same project would have to reason
    /// about a build that finished before it began.
    /// </remarks>
    Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the broker took the event: stamps the delivery time, counts the
    /// attempt, and clears any error left by an earlier one.
    /// </summary>
    Task MarkPublishedAsync(Guid eventId, DateTime publishedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt, leaving the event pending so the next pass picks it
    /// up again.
    /// </summary>
    /// <remarks>
    /// The error is written down rather than only logged: an event stuck at a rising
    /// attempt count is the thing somebody needs to see, and logs roll away while
    /// this row does not.
    /// </remarks>
    Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every event raised for one project, oldest first — delivered and pending
    /// alike. The read the tests assert a transition's announcements through.
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}
