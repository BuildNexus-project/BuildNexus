using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Data access for the <c>payment_outbox_events</c> table, from the dispatch
/// side.
/// </summary>
/// <remarks>
/// There is deliberately no insert here. An outbox row is only ever written in
/// the same transaction as the payment it describes, so it is written by
/// <see cref="PaymentRepository"/> alongside the payment — an event that could be
/// enqueued on its own would be an event that might commit while the payment it
/// announces rolled back.
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>
    /// The events still waiting to be sent, oldest first, up to
    /// <paramref name="batchSize"/>.
    /// </summary>
    /// <remarks>
    /// In sequence order, so two payments against one invoice reach the topic in
    /// the order they were made — a consumer that saw the settling payment before
    /// the partial one that preceded it would have to reason about a balance that
    /// went up.
    /// </remarks>
    Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the broker took the event: stamps the delivery time, counts
    /// the attempt, and clears any error left by an earlier one.
    /// </summary>
    Task MarkPublishedAsync(Guid eventId, DateTime publishedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt, leaving the event pending so the next pass picks
    /// it up again.
    /// </summary>
    /// <remarks>
    /// The error is written down rather than only logged: an event stuck at a
    /// rising attempt count is the thing somebody needs to see, and logs roll away
    /// while this row does not.
    /// </remarks>
    Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every event raised for one invoice, oldest first — delivered and pending
    /// alike. The read the tests assert a payment's announcements through.
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> ListForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}
