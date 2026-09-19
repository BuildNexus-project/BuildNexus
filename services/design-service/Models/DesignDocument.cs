namespace BuildNexus.DesignService.Models;

/// <summary>
/// A logical design document within one project — "GroundFloorPlan",
/// "Elevations". Its uploads are <see cref="DesignDocumentVersion"/> rows; a
/// second upload under the same name in the same project is a new version of
/// this row, not a new document.
/// </summary>
public class DesignDocument
{
    public Guid Id { get; set; }

    /// <summary>
    /// The project it belongs to. A plain column, not a foreign key — the
    /// project lives in the Project Service's own database.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>The name the versions are grouped under, unique within the project.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Architect who uploaded the first version, from their token's <c>sub</c> claim.</summary>
    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
