using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// The <c>payload</c> of a <see cref="DesignEventTypes.DesignSubmitted"/> event:
/// which version an Architect has just uploaded, of which document and project.
/// </summary>
/// <remarks>
/// No consumer subscribes to this yet (US-23 names it; nothing acts on it), but
/// it carries the same project and document context <see cref="DesignApprovedPayload"/>
/// does, so one can without calling back here.
/// </remarks>
public sealed class DesignSubmittedPayload
{
    public required Guid DocumentId { get; init; }

    public required Guid ProjectId { get; init; }

    public required string DocumentName { get; init; }

    public required Guid VersionId { get; init; }

    public required int VersionNumber { get; init; }

    /// <summary>The Architect who uploaded it.</summary>
    public required Guid SubmittedByUserId { get; init; }

    /// <summary>Offset-bearing, so the instant is unambiguous on the wire.</summary>
    public required DateTimeOffset SubmittedAt { get; init; }

    public static DesignSubmittedPayload From(DesignDocument document, DesignDocumentVersion version) =>
        new()
        {
            DocumentId = document.Id,
            ProjectId = document.ProjectId,
            DocumentName = document.Name,
            VersionId = version.Id,
            VersionNumber = version.VersionNumber,
            SubmittedByUserId = version.UploadedBy,
            SubmittedAt = EventTimestamp.AsUtc(version.UploadedAt)
        };
}
