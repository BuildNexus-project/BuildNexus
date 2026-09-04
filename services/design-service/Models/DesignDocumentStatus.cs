namespace BuildNexus.DesignService.Models;

/// <summary>
/// Where a design document version sits in review. Persisted as its string name
/// in <c>design_document_versions.status</c>.
/// </summary>
/// <remarks>
/// US-09 defines one value: every upload lands as <see cref="Submitted"/>. The
/// review outcomes that follow (approved, rejected) arrive with the story that
/// adds them, alongside the migration that widens
/// <c>ck_design_document_versions_status</c> to match. The name is what is
/// stored and compared, never the underlying number.
/// </remarks>
public enum DesignDocumentStatus
{
    /// <summary>Uploaded by the Architect and waiting on review.</summary>
    Submitted
}
