namespace BuildNexus.DesignService.Models;

/// <summary>
/// One version, read before a review decision is made on it: enough to check
/// the caller's access, name the Architect to notify, and build the Kafka
/// envelope — without yet writing anything.
/// </summary>
public class DesignVersionForReview
{
    public required Guid VersionId { get; init; }

    public required Guid DocumentId { get; init; }

    /// <summary>Checked against the caller with the Project Service before a decision is recorded.</summary>
    public required Guid ProjectId { get; init; }

    public required string DocumentName { get; init; }

    public required int VersionNumber { get; init; }

    /// <summary>The Architect who uploaded this version — who a revision request notifies.</summary>
    public required Guid UploadedBy { get; init; }

    public required DesignDocumentStatus Status { get; init; }
}
