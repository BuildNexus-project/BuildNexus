using System.Text.Json;
using BuildNexus.DesignService.Messaging;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The <see cref="DesignEvents"/> factories (US-23): the event type each one
/// stamps, the id the outbox row shares with its envelope, and the envelope
/// JSON that goes on the wire — the contract the Construction Service reads on
/// the other side of <c>design-events</c>.
/// </summary>
public class DesignEventsTests
{
    private static readonly Guid DocumentId = Guid.Parse("d0c00000-0000-4000-8000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("9f01d000-0000-4000-8000-000000000009");
    private static readonly Guid VersionId = Guid.Parse("7e560000-0000-4000-8000-000000000002");
    private static readonly Guid ArchitectId = Guid.Parse("a11ce000-0000-4000-8000-000000000003");
    private static readonly Guid ClientId = Guid.Parse("c11e0000-0000-4000-8000-000000000004");
    private static readonly DateTime OccurredAt = new(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Submitted_stamps_DesignSubmitted_and_carries_the_project_and_uploader()
    {
        var document = Document();
        var version = Version(DesignDocumentStatus.Submitted);

        var row = DesignEvents.Submitted(document, version);

        Assert.Equal("DesignSubmitted", row.EventType);
        Assert.Equal(DocumentId, row.DocumentId);
        Assert.Equal(version.UploadedAt, row.OccurredAt);

        var envelope = Parse(row);
        Assert.Equal("DesignSubmitted", envelope.GetProperty("eventType").GetString());
        // The row and the envelope share an id, so a message traces back to the row.
        Assert.Equal(row.Id, envelope.GetProperty("eventId").GetGuid());

        var payload = envelope.GetProperty("payload");
        Assert.Equal(ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(DocumentId, payload.GetProperty("documentId").GetGuid());
        Assert.Equal(2, payload.GetProperty("versionNumber").GetInt32());
        Assert.Equal(ArchitectId, payload.GetProperty("submittedByUserId").GetGuid());
        Assert.Equal(version.UploadedAt, payload.GetProperty("submittedAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public void RevisionRequested_stamps_DesignRevisionRequested_and_carries_the_comment()
    {
        var row = DesignEvents.RevisionRequested(VersionForReview(), ClientId, "Move the stairs to the east wall.", OccurredAt);

        Assert.Equal("DesignRevisionRequested", row.EventType);
        Assert.Equal(DocumentId, row.DocumentId);

        var payload = Parse(row).GetProperty("payload");
        Assert.Equal(ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(ClientId, payload.GetProperty("requestedByUserId").GetGuid());
        Assert.Equal("Move the stairs to the east wall.", payload.GetProperty("comment").GetString());
        Assert.Equal(OccurredAt, payload.GetProperty("requestedAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public void Approved_stamps_DesignApproved_with_the_fields_construction_reads()
    {
        var row = DesignEvents.Approved(VersionForReview(), ClientId, OccurredAt);

        Assert.Equal("DesignApproved", row.EventType);
        Assert.Equal(DocumentId, row.DocumentId);
        Assert.Equal(OccurredAt, row.OccurredAt);

        var envelope = Parse(row);
        // The Construction Service binds exactly these envelope fields
        // (IncomingEvent) and payload fields (DesignApprovedPayload).
        Assert.Equal("DesignApproved", envelope.GetProperty("eventType").GetString());
        Assert.Equal(row.Id, envelope.GetProperty("eventId").GetGuid());
        Assert.True(envelope.TryGetProperty("occurredAt", out _));

        var payload = envelope.GetProperty("payload");
        Assert.Equal(ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(DocumentId, payload.GetProperty("documentId").GetGuid());
        Assert.Equal(OccurredAt, payload.GetProperty("approvedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(ClientId, payload.GetProperty("approvedByUserId").GetGuid());
    }

    private static JsonElement Parse(OutboxEvent row) => JsonDocument.Parse(row.Envelope).RootElement;

    private static DesignDocument Document() => new()
    {
        Id = DocumentId,
        ProjectId = ProjectId,
        Name = "GroundFloorPlan",
        CreatedBy = ArchitectId,
        CreatedAt = OccurredAt
    };

    private static DesignDocumentVersion Version(DesignDocumentStatus status) => new()
    {
        Id = VersionId,
        DocumentId = DocumentId,
        VersionNumber = 2,
        FileName = "plan.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 1024,
        Status = status,
        UploadedBy = ArchitectId,
        UploadedAt = OccurredAt
    };

    private static DesignVersionForReview VersionForReview() => new()
    {
        VersionId = VersionId,
        DocumentId = DocumentId,
        ProjectId = ProjectId,
        DocumentName = "GroundFloorPlan",
        VersionNumber = 2,
        UploadedBy = ArchitectId,
        Status = DesignDocumentStatus.Submitted
    };
}
