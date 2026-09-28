using BuildNexus.PaymentService.Data;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An in-memory <see cref="IConstructionCompletionRepository"/> for the consumer
/// suite.
/// </summary>
/// <remarks>
/// Records what it was told and nothing more. The settlement rule itself — the
/// two conditions, the row lock, the announce-once marker — is enforced in SQL
/// and covered against the real engine in
/// <see cref="FinalPaymentSettledDatabaseTests"/>; what the consumer suite needs
/// to know is only that the event reached the repository.
/// </remarks>
public class FakeConstructionCompletionRepository : IConstructionCompletionRepository
{
    private readonly HashSet<Guid> _complete = [];

    /// <summary>Every completion the consumer recorded, in order.</summary>
    public List<(Guid ProjectId, DateTime CompletedAtUtc, Guid SourceEventId)> Recorded { get; } = [];

    /// <summary>What the next record call should report back as having announced.</summary>
    public bool NextAnnounces { get; set; }

    /// <summary>Set to make the next write throw, standing in for a database that is down.</summary>
    public Exception? NextWriteThrows { get; set; }

    public Task<bool> RecordCompletionAndMaybeAnnounceAsync(
        Guid projectId,
        DateTime completedAtUtc,
        Guid sourceEventId,
        CancellationToken cancellationToken = default)
    {
        if (NextWriteThrows is not null)
        {
            var toThrow = NextWriteThrows;
            NextWriteThrows = null;
            throw toThrow;
        }

        Recorded.Add((projectId, completedAtUtc, sourceEventId));
        _complete.Add(projectId);

        return Task.FromResult(NextAnnounces);
    }

    public Task<bool> IsCompleteAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_complete.Contains(projectId));

    public Task<bool> HasAnnouncedAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
