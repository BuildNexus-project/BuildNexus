using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// An outbox held in memory, standing in for the table.
/// </summary>
/// <remarks>
/// Shared by the controller suites and the dispatcher's own, so all three are
/// provably talking to the same shape rather than each keeping a stub that
/// drifted from the interface.
/// <para>
/// Only the marks are modelled properly — set a delivery time, count the
/// attempt, clear or record an error — because that is the part the dispatcher
/// depends on being right. Enqueueing is deliberately absent here as it is on
/// the interface: an event is only ever written inside the transaction that
/// made the change, so tests seed rows with <see cref="Seed"/> instead.
/// </para>
/// </remarks>
public sealed class FakeOutboxRepository : IOutboxRepository
{
    private readonly List<OutboxEvent> _events = [];

    /// <summary>How many times the pending batch has been asked for.</summary>
    public int PendingReads { get; private set; }

    /// <summary>The batch size the last read asked for.</summary>
    public int LastBatchSize { get; private set; }

    /// <summary>Everything the outbox holds, in the order it was seeded.</summary>
    public IReadOnlyList<OutboxEvent> Events => _events;

    public void Seed(params OutboxEvent[] events) => _events.AddRange(events);

    public Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        PendingReads++;
        LastBatchSize = batchSize;

        // Oldest first and capped, the way the SQL orders and limits.
        return Task.FromResult<IReadOnlyList<OutboxEvent>>(
            [.. _events.Where(e => !e.IsPublished).OrderBy(e => e.SequenceNumber).Take(batchSize)]);
    }

    public Task MarkPublishedAsync(
        Guid eventId,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var found = Find(eventId);

        if (found is null || found.IsPublished)
        {
            // The real UPDATE is guarded on published_at IS NULL, so marking a
            // delivered event twice writes nothing.
            return Task.CompletedTask;
        }

        found.PublishedAt = publishedAtUtc;
        found.AttemptCount++;
        found.LastError = null;

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        var found = Find(eventId);

        if (found is null || found.IsPublished)
        {
            return Task.CompletedTask;
        }

        found.AttemptCount++;
        found.LastError = error;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxEvent>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OutboxEvent>>(
            [.. _events.Where(e => e.ProjectId == projectId).OrderBy(e => e.SequenceNumber)]);

    private OutboxEvent? Find(Guid eventId) => _events.SingleOrDefault(e => e.Id == eventId);
}
