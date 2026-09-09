using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Data;

/// <summary>
/// Data access for <c>design_documents</c> and the
/// <c>design_document_versions</c> that belong to them.
/// </summary>
public interface IDesignDocumentRepository
{
    /// <summary>
    /// Records one upload: finds the document for
    /// <see cref="DesignUpload.ProjectId"/> and
    /// <see cref="DesignUpload.DocumentName"/> — creating it if this is the
    /// first upload under that name — and appends the next version in sequence.
    /// </summary>
    /// <remarks>
    /// The document lookup-or-create and the version insert happen in one
    /// transaction, and the version number is <c>MAX + 1</c> taken inside it.
    /// Two uploads racing for the same next number is caught by
    /// <c>uq_design_document_versions_number</c> and retried, so a version
    /// number is never skipped or reused.
    /// </remarks>
    Task<DesignUploadResult> AddVersionAsync(DesignUpload upload);

    /// <summary>
    /// Every design document for a project, each with its versions — oldest
    /// document and oldest version first. Metadata only; no file bytes.
    /// </summary>
    Task<IReadOnlyList<DesignDocumentWithVersions>> ListForProjectAsync(Guid projectId);

    /// <summary>
    /// One version's stored file, or <c>null</c> if there is no version with
    /// that id. Carries the owning project so the caller's access can be
    /// checked before the bytes are returned.
    /// </summary>
    Task<StoredDesignFile?> GetVersionFileAsync(Guid versionId);

    /// <summary>
    /// One version, read for a review decision — or <c>null</c> if there is no
    /// version with that id. Read before <see cref="RecordReviewDecisionAsync"/>
    /// so the caller's project access can be checked first.
    /// </summary>
    Task<DesignVersionForReview?> GetVersionForReviewAsync(Guid versionId);

    /// <summary>
    /// Records a Client's decision on one version (US-11) — approve, or request
    /// a revision with a comment.
    /// </summary>
    /// <remarks>
    /// Refuses rather than overwrites: a version that is no longer
    /// <see cref="Models.DesignDocumentStatus.Submitted"/>, or whose document
    /// already has an <see cref="Models.DesignDocumentStatus.Approved"/> version,
    /// has already had its decision made. Both checks happen inside the same
    /// transaction as the write, with the document row locked for its length —
    /// two reviews racing for the same document resolve one after the other,
    /// never both.
    /// </remarks>
    Task<ReviewDecisionOutcome> RecordReviewDecisionAsync(ReviewDecision decision);
}
