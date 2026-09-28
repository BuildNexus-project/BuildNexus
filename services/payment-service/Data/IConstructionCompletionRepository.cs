namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Data access for <c>construction_completions</c>, and the announcement of a
/// project's final settlement.
/// </summary>
/// <remarks>
/// The replica of one fact the Construction Service owns — that a project's
/// build is finished — kept locally so this service can tell "nothing is
/// outstanding right now" from "nothing more will ever be billed". Only the
/// second of those means the final payment has landed.
/// </remarks>
public interface IConstructionCompletionRepository
{
    /// <summary>
    /// Records that a project's build is complete, unless it is already
    /// recorded, and announces the final settlement if that was the last thing
    /// the project was waiting on.
    /// </summary>
    /// <remarks>
    /// Idempotent: Kafka delivers at least once, so a redelivered
    /// <c>ConstructionCompleted</c> must be absorbed rather than becoming an
    /// error that wedges the consumer. The <c>project_id</c> primary key does
    /// the absorbing, and the announcement is guarded separately so a
    /// redelivery cannot announce twice.
    /// </remarks>
    /// <returns><c>true</c> when this call announced the final settlement.</returns>
    Task<bool> RecordCompletionAndMaybeAnnounceAsync(
        Guid projectId,
        DateTime completedAtUtc,
        Guid sourceEventId,
        CancellationToken cancellationToken = default);

    /// <summary>Whether this service has recorded the project's build as complete.</summary>
    Task<bool> IsCompleteAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Whether the final settlement has already been announced for this project.</summary>
    Task<bool> HasAnnouncedAsync(Guid projectId, CancellationToken cancellationToken = default);
}
