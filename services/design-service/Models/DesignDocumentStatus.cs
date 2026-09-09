namespace BuildNexus.DesignService.Models;

/// <summary>
/// Where a design document version sits in review. Persisted as its string name
/// in <c>design_document_versions.status</c>.
/// </summary>
/// <remarks>
/// Every upload still lands as <see cref="Submitted"/> — nothing in this
/// service moves a version off it yet, that arrives with the review-workflow
/// story. <see cref="UnderReview"/> and <see cref="Approved"/> exist now so
/// US-10 can identify the current version: the latest one in either of those
/// two states. <c>Rejected</c> is deliberately not here — add it, and widen
/// <c>ck_design_document_versions_status</c> again, alongside the story that
/// first sets it. The name is what is stored and compared, never the
/// underlying number.
/// </remarks>
public enum DesignDocumentStatus
{
    /// <summary>Uploaded by the Architect and waiting on review.</summary>
    Submitted,

    /// <summary>A reviewer has picked it up; no decision is recorded yet.</summary>
    UnderReview,

    /// <summary>Signed off. The version US-10 shows as current, until a later one is.</summary>
    Approved
}
