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

    /// <summary>The last decision the controller asked to record, or <c>null</c> if none.</summary>
    public ReviewDecision? LastReviewDecision { get; private set; }

    /// <summary>The outbox events the controller asked to record alongside the last decision.</summary>
    public IReadOnlyList<OutboxEvent> LastOutboxEvents { get; private set; } = [];

    /// <summary>The outbox events the last upload's callback produced.</summary>
    public IReadOnlyList<OutboxEvent> LastUploadOutboxEvents { get; private set; } = [];

    public Task<DesignUploadResult> AddVersionAsync(
        DesignUpload upload,
        Func<DesignDocument, DesignDocumentVersion, IReadOnlyList<OutboxEvent>> buildOutboxEvents)
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

        // Called the same way the real repository calls it — with the document
        // and version this "transaction" just created.
        LastUploadOutboxEvents = buildOutboxEvents(document, version);

        return Task.FromResult(new DesignUploadResult { Document = document, Version = version });
    }

    public Task<IReadOnlyList<DesignDocumentWithVersions>> ListForProjectAsync(Guid projectId) =>
        Task.FromResult<IReadOnlyList<DesignDocumentWithVersions>>(
            [.. Documents.Where(d => d.Document.ProjectId == projectId)]);

    public Task<StoredDesignFile?> GetVersionFileAsync(Guid versionId) =>
        Task.FromResult(StoredFile is { } file && file.VersionId == versionId ? file : null);

    public Task<DesignVersionForReview?> GetVersionForReviewAsync(Guid versionId)
    {
        var found = Documents
            .Select(entry => (Entry: entry, Version: entry.Versions.FirstOrDefault(v => v.Id == versionId)))
            .FirstOrDefault(pair => pair.Version is not null);

        if (found.Version is null)
        {
            return Task.FromResult<DesignVersionForReview?>(null);
        }

        return Task.FromResult<DesignVersionForReview?>(new DesignVersionForReview
        {
            VersionId = found.Version.Id,
            DocumentId = found.Entry.Document.Id,
            ProjectId = found.Entry.Document.ProjectId,
            DocumentName = found.Entry.Document.Name,
            VersionNumber = found.Version.VersionNumber,
            UploadedBy = found.Version.UploadedBy,
            Status = found.Version.Status
        });
    }

    /// <summary>
    /// Applies the same rules <see cref="Data.DesignDocumentRepository"/> does —
    /// refuse a version that is not Submitted, or a document with an Approved
    /// version already — over the in-memory <see cref="Documents"/> instead of
    /// SQL, so the controller suite can pin the refusals without MySQL.
    /// </summary>
    public Task<ReviewDecisionOutcome> RecordReviewDecisionAsync(
        ReviewDecision decision, IReadOnlyList<OutboxEvent> outboxEvents)
    {
        LastReviewDecision = decision;
        LastOutboxEvents = outboxEvents;

        var entry = Documents.FirstOrDefault(d => d.Document.Id == decision.DocumentId);
        var version = entry?.Versions.FirstOrDefault(v => v.Id == decision.VersionId);

        if (entry is null || version is null)
        {
            return Task.FromResult(ReviewDecisionOutcome.VersionNotFound);
        }

        if (version.Status != DesignDocumentStatus.Submitted)
        {
            return Task.FromResult(ReviewDecisionOutcome.AlreadyDecided);
        }

        if (entry.Versions.Any(v => v.Id != decision.VersionId && v.Status == DesignDocumentStatus.Approved))
        {
            return Task.FromResult(ReviewDecisionOutcome.DocumentAlreadyApproved);
        }

        version.Status = decision.Status;
        version.ReviewedBy = decision.ReviewedBy;
        version.ReviewedAt = decision.ReviewedAtUtc;
        version.ReviewComment = decision.ReviewComment;

        return Task.FromResult(ReviewDecisionOutcome.Recorded);
    }
}
