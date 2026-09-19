using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// One uploaded version of a design document, with the metadata US-09 asks to
/// be kept beside it: the version number, upload date, uploader, status, and
/// revision comment.
/// </summary>
public class DesignVersionResponse
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>The document's name, repeated here so a version stands on its own.</summary>
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>The project the document belongs to.</summary>
    public Guid ProjectId { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>
    /// The label the story shows — <c>GroundFloorPlan_v2</c> — built from the
    /// document name and this version's number.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The name the file had when it was uploaded, used for the download.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The type the file's own bytes say it is — <c>application/pdf</c>, <c>image/jpeg</c> or <c>image/png</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>The status name — <c>Submitted</c> until a review records a decision.</summary>
    public string Status { get; set; } = string.Empty;

    public string? RevisionComment { get; set; }

    /// <summary>The Architect who uploaded this version, as an id — the account lives in the User Service.</summary>
    public Guid UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }

    /// <summary>
    /// True for the one version US-10 shows as current: the latest, by version
    /// number, that is <see cref="DesignDocumentStatus.UnderReview"/> or
    /// <see cref="DesignDocumentStatus.Approved"/>. False for every version of a
    /// document where none has reached either state yet — nothing is highlighted
    /// rather than a <c>Submitted</c> version standing in for one.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// The Client who approved this version or asked for a revision on it
    /// (US-11), or <c>null</c> before either has happened.
    /// </summary>
    public Guid? ReviewedBy { get; set; }

    /// <summary><c>null</c> until <see cref="ReviewedBy"/> is set — the two always arrive together.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// The Client's note on what needs to change — the whole point of showing
    /// this to the Architect. Set only when the decision was a request for
    /// revision; <c>null</c> for an approval or a version nobody has reviewed.
    /// </summary>
    public string? ReviewComment { get; set; }

    public static DesignVersionResponse From(DesignDocument document, DesignDocumentVersion version, bool isCurrent) => new()
    {
        Id = version.Id,
        DocumentId = version.DocumentId,
        DocumentName = document.Name,
        ProjectId = document.ProjectId,
        VersionNumber = version.VersionNumber,
        DisplayName = $"{document.Name}_v{version.VersionNumber}",
        FileName = version.FileName,
        ContentType = version.ContentType,
        FileSizeBytes = version.FileSizeBytes,
        Status = version.Status.ToString(),
        RevisionComment = version.RevisionComment,
        UploadedBy = version.UploadedBy,
        UploadedAt = version.UploadedAt,
        IsCurrent = isCurrent,
        ReviewedBy = version.ReviewedBy,
        ReviewedAt = version.ReviewedAt,
        ReviewComment = version.ReviewComment
    };
}
