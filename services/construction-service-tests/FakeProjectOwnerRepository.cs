using BuildNexus.ConstructionService.Data;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IProjectOwnerRepository"/> that records the calls made to it
/// and answers ownership from an in-memory map, so the consumer and controller
/// suites can check behaviour without MySQL. The real <c>INSERT IGNORE</c> and
/// <c>EXISTS</c> are covered by <see cref="ProjectOwnerRepositoryDatabaseTests"/>.
/// </summary>
public sealed class FakeProjectOwnerRepository : IProjectOwnerRepository
{
    /// <summary>One entry per <see cref="RecordOwnerIfAbsentAsync"/> call, in order.</summary>
    public List<OwnerCall> Calls { get; } = [];

    /// <summary>
    /// The ownership <see cref="IsOwnedByAsync"/> answers from: project id to
    /// the Client that owns it. A project absent from the map is unowned as far
    /// as the check is concerned, which is the "this service has not learned
    /// who owns it yet" case.
    /// </summary>
    public Dictionary<Guid, Guid> Owners { get; } = [];

    /// <summary>When set, <see cref="RecordOwnerIfAbsentAsync"/> throws it — a stand-in for the database being unreachable.</summary>
    public Exception? RecordThrows { get; set; }

    /// <summary>What <see cref="RecordOwnerIfAbsentAsync"/> returns when it does not throw.</summary>
    public bool RecordResult { get; set; } = true;

    public Task<bool> RecordOwnerIfAbsentAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new OwnerCall(projectId, clientId));

        if (RecordThrows is not null)
        {
            throw RecordThrows;
        }

        return Task.FromResult(RecordResult);
    }

    public Task<bool> IsOwnedByAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Owners.TryGetValue(projectId, out var owner) && owner == clientId);

    public readonly record struct OwnerCall(Guid ProjectId, Guid ClientId);
}
