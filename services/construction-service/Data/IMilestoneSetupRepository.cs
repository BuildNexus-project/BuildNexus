using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>Data access for <c>milestone_setups</c>.</summary>
public interface IMilestoneSetupRepository
{
    /// <summary>
    /// Creates the milestone-setup placeholder for a project, unless one is
    /// already there.
    /// </summary>
    /// <remarks>
    /// Idempotent, deliberately: a project's second approved design document, or
    /// a Kafka redelivery of the same <c>DesignApproved</c> event, must not
    /// create a second row and is not an error — the <c>project_id</c> unique
    /// index absorbs it. Returns <c>true</c> only when a row was actually
    /// created.
    /// </remarks>
    Task<bool> CreatePlaceholderIfAbsentAsync(
        Guid projectId,
        Guid sourceDocumentId,
        Guid sourceEventId,
        DateTime approvedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Every placeholder, newest first.</summary>
    Task<IReadOnlyList<MilestoneSetup>> ListAsync(CancellationToken cancellationToken = default);
}
