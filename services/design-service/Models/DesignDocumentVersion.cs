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
}
