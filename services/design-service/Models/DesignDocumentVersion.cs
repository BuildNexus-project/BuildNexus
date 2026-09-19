namespace BuildNexus.DesignService.Models;

/// <summary>
/// One upload of a <see cref="DesignDocument"/> — the file, and the metadata
/// US-09 asks to be kept beside it: version number, upload date, uploader,
/// status, and the Architect's revision comment.
/// </summary>
/// <remarks>
/// Metadata only — the file bytes are not carried here. A listing reads many of
/// these and never needs the content; the download reads it on its own through
/// <see cref="StoredDesignFile"/>.
/// </remarks>
public class DesignDocumentVersion
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>
    /// 1 for the first upload, then 2, 3, ... The <c>GroundFloorPlan_v2</c>
    /// label the story shows is this number and the document's name, built from
    /// the two rather than stored.
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>What the Architect called the file on their machine.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The validated content type — one of the three the story allows.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DesignDocumentStatus Status { get; set; }

    /// <summary>The Architect's note on what changed in this revision, or <c>null</c>.</summary>
    public string? RevisionComment { get; set; }

    /// <summary>The Architect who uploaded this version, from their token's <c>sub</c> claim.</summary>
    public Guid UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }

    /// <summary>
    /// The Client who approved or requested a revision on this version (US-11),
    /// or <c>null</c> before either has happened.
    /// </summary>
    public Guid? ReviewedBy { get; set; }

    /// <summary><c>null</c> until <see cref="ReviewedBy"/> is set — the two always arrive together.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// The Client's note on what needs to change, set only when the decision was
    /// a request for revision. Kept separate from <see cref="RevisionComment"/>,
    /// which is the Architect's own note on this upload.
    /// </summary>
    public string? ReviewComment { get; set; }
}
