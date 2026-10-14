namespace BuildNexus.DesignService.Models;

/// <summary>
/// One design document whose latest version is a revision the Client asked for
/// and the Architect has not yet answered (US-21 AC-2).
/// </summary>
/// <remarks>
/// "Pending" is the state of the document's <em>latest</em> version. Once the
/// Architect uploads a newer one the revision is answered and the document drops
/// off the list, whatever became of the newer version.
/// </remarks>
public class PendingRevision
{
    public required Guid ProjectId { get; init; }

    public required Guid DocumentId { get; init; }

    /// <summary>The document's name — "GroundFloorPlan", "Elevations".</summary>
    public required string DocumentName { get; init; }

    /// <summary>The version the Client sent back for changes.</summary>
    public required int VersionNumber { get; init; }

    /// <summary>What the Client asked to change. The revision is only ever recorded with one.</summary>
    public string? ReviewComment { get; init; }

    /// <summary>When the Client asked — how long the Architect has been holding the revision.</summary>
    public required DateTime RequestedAtUtc { get; init; }
}
