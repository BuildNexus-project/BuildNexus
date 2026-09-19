namespace BuildNexus.DesignService.Models;

/// <summary>
/// One design document upload, as the controller hands it to the repository:
/// the project and document name it belongs under, the file, and the metadata
/// to record with it.
/// </summary>
/// <remarks>
/// Not a <see cref="DesignDocumentVersion"/> because two of that row's fields
/// are the repository's to decide, not the caller's: the version number (the
/// next in sequence for the document) and the document id (found, or created if
/// this is the first upload under this name).
/// </remarks>
public class DesignUpload
{
    public required Guid ProjectId { get; init; }

    /// <summary>The name to group this version under. A new name starts a new document at v1.</summary>
    public required string DocumentName { get; init; }

    public required Guid UploadedBy { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }

    public string? RevisionComment { get; init; }

    public required DateTime UploadedAtUtc { get; init; }
}
