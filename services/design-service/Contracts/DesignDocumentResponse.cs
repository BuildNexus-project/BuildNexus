using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// One design document and every version uploaded under it, oldest first — so
/// the latest work is the last entry and the whole history is there beside it.
/// </summary>
public class DesignDocumentResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>The name the versions are grouped under, e.g. <c>GroundFloorPlan</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Architect who uploaded version 1.</summary>
    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The latest version's number — what <c>{Name}_v{n}</c> currently resolves to.</summary>
    public int LatestVersionNumber { get; set; }

    public IReadOnlyList<DesignVersionResponse> Versions { get; set; } = [];

    public static DesignDocumentResponse From(DesignDocumentWithVersions document) => new()
    {
        Id = document.Document.Id,
        ProjectId = document.Document.ProjectId,
        Name = document.Document.Name,
        CreatedBy = document.Document.CreatedBy,
        CreatedAt = document.Document.CreatedAt,
        LatestVersionNumber = document.Versions.Count == 0 ? 0 : document.Versions[^1].VersionNumber,
        Versions = [.. document.Versions.Select(version => DesignVersionResponse.From(document.Document, version))]
    };
}
