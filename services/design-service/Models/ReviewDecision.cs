namespace BuildNexus.DesignService.Models;

/// <summary>
/// A Client's decision on one version, as the controller hands it to the
/// repository (US-11).
/// </summary>
public class ReviewDecision
{
    public required Guid VersionId { get; init; }

    public required Guid DocumentId { get; init; }

    /// <summary><see cref="DesignDocumentStatus.Approved"/> or <see cref="DesignDocumentStatus.RevisionRequested"/> — never anything else.</summary>
    public required DesignDocumentStatus Status { get; init; }

    public required Guid ReviewedBy { get; init; }

    public required DateTime ReviewedAtUtc { get; init; }

    /// <summary>The Client's note on what needs to change. Null for an approval.</summary>
    public string? ReviewComment { get; init; }
}
