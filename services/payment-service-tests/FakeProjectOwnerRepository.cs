using BuildNexus.PaymentService.Data;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An in-memory <see cref="IProjectOwnerRepository"/> for the consumer and
/// controller suites.
/// </summary>
/// <remarks>
/// The SQL itself — the <c>INSERT IGNORE</c> that absorbs a redelivered event,
/// and the <c>EXISTS</c> that answers the pair — is covered against the real
/// engine in <see cref="ProjectOwnerRepositoryDatabaseTests"/>.
/// </remarks>
public class FakeProjectOwnerRepository : IProjectOwnerRepository
{
    private readonly Dictionary<Guid, Guid> _owners = [];

    /// <summary>Set to make the next write throw, standing in for a database that is down.</summary>
    public Exception? NextWriteThrows { get; set; }

    public Task<bool> RecordOwnerIfAbsentAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        if (NextWriteThrows is not null)
        {
            var toThrow = NextWriteThrows;
            NextWriteThrows = null;
            throw toThrow;
        }

        // Mirrors INSERT IGNORE on the project_id primary key: the first owner
        // learned of wins and a repeat is absorbed rather than overwriting.
        if (!_owners.TryAdd(projectId, clientId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task<bool> IsOwnedByAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_owners.TryGetValue(projectId, out var owner) && owner == clientId);

    /// <summary>The owner this fake holds for a project, or null if it has none.</summary>
    public Guid? OwnerOf(Guid projectId) =>
        _owners.TryGetValue(projectId, out var owner) ? owner : null;

    /// <summary>Plants an owner directly, for tests about reading rather than consuming.</summary>
    public void GiveOwnership(Guid projectId, Guid clientId) => _owners[projectId] = clientId;
}
