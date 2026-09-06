namespace BuildNexus.DesignService.Models;

/// <summary>
/// One version's stored file, read for a download: the bytes, what to call them,
/// and the project they belong to so the caller's access can be checked before
/// anything is sent back.
/// </summary>
public class StoredDesignFile
{
    public required Guid VersionId { get; init; }

    /// <summary>
    /// The project the owning document belongs to. Checked against the caller
    /// with the Project Service before the bytes leave this service.
    /// </summary>
    public required Guid ProjectId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}
