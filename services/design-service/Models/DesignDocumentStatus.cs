namespace BuildNexus.DesignService.Models;

/// <summary>
/// Where a design document version sits in review. Persisted as its string name
/// in <c>design_document_versions.status</c>.
/// </summary>
/// <remarks>
/// Every upload lands as <see cref="Submitted"/>. US-11's Client review moves
/// it from there straight to <see cref="Approved"/> or
/// <see cref="RevisionRequested"/> — nothing in this service sets
/// <see cref="UnderReview"/> yet, that stays reserved for a future "a reviewer
/// has picked this up" step. <c>Rejected</c> is deliberately still not here:
/// nothing sets it either, and the constraint can be widened again, the same
/// way this one widens 002, when a story does. The name is what is stored and
/// compared, never the underlying number.
/// </remarks>
public enum DesignDocumentStatus
{
    /// <summary>Uploaded by the Architect and waiting on review.</summary>
    Submitted,

    /// <summary>A reviewer has picked it up; no decision is recorded yet. Nothing sets this today.</summary>
    UnderReview,

    /// <summary>
    /// Signed off by the Client (US-11). The version US-10 shows as current,
    /// until a later one is — and once set, every other version of the same
    /// document is read-only: no further review decision may be made on them.
    /// </summary>
    Approved,

    /// <summary>
    /// The Client asked for changes (US-11), with <c>review_comment</c> saying
    /// what. Never current — only <see cref="UnderReview"/> or
    /// <see cref="Approved"/> qualify.
    /// </summary>
    RevisionRequested
}
