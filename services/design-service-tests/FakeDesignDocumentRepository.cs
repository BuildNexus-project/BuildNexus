using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// An <see cref="IDesignDocumentRepository"/> held in memory, standing in for
/// the tables so the controller suite needs no MySQL.
/// </summary>
/// <remarks>
/// The version-number rules and the SQL are covered by
/// <see cref="DesignDocumentRepositoryDatabaseTests"/> against the real engine;
/// here the point is what the controller does with the pieces, so
/// <see cref="AddVersionAsync"/> just records the upload and hands back a
/// plausible result.
/// </remarks>
public sealed class FakeDesignDocumentRepository : IDesignDocumentRepository
{
    private int _version;

    /// <summary>The last upload the controller asked to store, or <c>null</c> if none.</summary>
    public DesignUpload? LastUpload { get; private set; }

    /// <summary>What <see cref="ListForProjectAsync"/> returns, filtered by project.</summary>
    public List<DesignDocumentWithVersions> Documents { get; } = [];

    /// <summary>What <see cref="GetVersionFileAsync"/> returns, when its id is asked for.</summary>
    public StoredDesignFile? StoredFile { get; set; }

    public Task<DesignUploadResult> AddVersionAsync(DesignUpload upload)
    {
        LastUpload = upload;

        var document = new DesignDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = upload.ProjectId,
            Name = upload.DocumentName,
            CreatedBy = upload.UploadedBy,
            CreatedAt = upload.UploadedAtUtc
        };

        var version = new DesignDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            VersionNumber = ++_version,
            FileName = upload.FileName,
            ContentType = upload.ContentType,
            FileSizeBytes = upload.Content.LongLength,
            Status = DesignDocumentStatus.Submitted,
            RevisionComment = upload.RevisionComment,
            UploadedBy = upload.UploadedBy,
            UploadedAt = upload.UploadedAtUtc
        };

        return Task.FromResult(new DesignUploadResult { Document = document, Version = version });
    }

    public Task<IReadOnlyList<DesignDocumentWithVersions>> ListForProjectAsync(Guid projectId) =>
        Task.FromResult<IReadOnlyList<DesignDocumentWithVersions>>(
            [.. Documents.Where(d => d.Document.ProjectId == projectId)]);

    public Task<StoredDesignFile?> GetVersionFileAsync(Guid versionId) =>
        Task.FromResult(StoredFile is { } file && file.VersionId == versionId ? file : null);
}
