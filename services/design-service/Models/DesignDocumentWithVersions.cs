namespace BuildNexus.DesignService.Models;

/// <summary>
/// One design document and every version uploaded under it, oldest version
/// first — the shape the project listing is built from.
/// </summary>
public class DesignDocumentWithVersions
{
    public required DesignDocument Document { get; init; }

    public required IReadOnlyList<DesignDocumentVersion> Versions { get; init; }
}
