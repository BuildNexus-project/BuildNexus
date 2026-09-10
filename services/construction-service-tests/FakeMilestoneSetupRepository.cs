using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IMilestoneSetupRepository"/> that records the calls made to it,
/// so the consumer and controller suites can check behaviour without MySQL. The
/// real <c>INSERT IGNORE</c> is covered by
/// <see cref="MilestoneSetupRepositoryDatabaseTests"/>.
/// </summary>
public sealed class FakeMilestoneSetupRepository : IMilestoneSetupRepository
{
    /// <summary>One entry per <see cref="CreatePlaceholderIfAbsentAsync"/> call, in order.</summary>
    public List<PlaceholderCall> Calls { get; } = [];

    /// <summary>Rows <see cref="ListAsync"/> returns.</summary>
    public IReadOnlyList<MilestoneSetup> Rows { get; set; } = [];

    /// <summary>When set, <see cref="CreatePlaceholderIfAbsentAsync"/> throws it — a stand-in for the database being unreachable.</summary>
    public Exception? CreateThrows { get; set; }

    /// <summary>What <see cref="CreatePlaceholderIfAbsentAsync"/> returns when it does not throw.</summary>
    public bool CreateResult { get; set; } = true;

    public Task<bool> CreatePlaceholderIfAbsentAsync(
        Guid projectId,
        Guid sourceDocumentId,
        Guid sourceEventId,
        DateTime approvedAtUtc,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new PlaceholderCall(projectId, sourceDocumentId, sourceEventId, approvedAtUtc));

        if (CreateThrows is not null)
        {
            throw CreateThrows;
        }

        return Task.FromResult(CreateResult);
    }

    public Task<IReadOnlyList<MilestoneSetup>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows);

    public readonly record struct PlaceholderCall(
        Guid ProjectId,
        Guid SourceDocumentId,
        Guid SourceEventId,
        DateTime ApprovedAtUtc);
}
