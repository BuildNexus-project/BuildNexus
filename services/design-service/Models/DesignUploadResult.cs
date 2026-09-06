namespace BuildNexus.DesignService.Models;

/// <summary>
/// What a stored upload produced: the document it landed under — newly created
/// or already there — and the version row written for it.
/// </summary>
/// <remarks>
/// Both are returned because the response needs each: the document's name and
/// the version's number together make the <c>GroundFloorPlan_v2</c> label, and
/// whether the document was new tells the caller a fresh document line was
/// started rather than a revision added.
/// </remarks>
public class DesignUploadResult
{
    public required DesignDocument Document { get; init; }

    public required DesignDocumentVersion Version { get; init; }

    /// <summary><c>true</c> when this upload created the document, i.e. it is version 1.</summary>
    public bool DocumentWasCreated => Version.VersionNumber == 1;
}
