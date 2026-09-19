using BuildNexus.DesignService.Messaging;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The parts of <see cref="BuildNexus.DesignService.Data.DesignDocumentRepository"/>
/// that only the real database can answer, run against real MySQL: the
/// version-number increment, the find-or-create, and the file round trip
/// through a <c>LONGBLOB</c>.
/// </summary>
/// <remarks>
/// Needs <c>design-db</c> running — see <see cref="DesignDatabaseFixture"/>.
/// </remarks>
[Collection(DesignDatabaseCollection.Name)]
public class DesignDocumentRepositoryDatabaseTests
{
    private static readonly DateTime UploadedAt = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

    private readonly DesignDatabaseFixture _fixture;

    /// <summary>
    /// Eight hex characters unique to this test. xUnit builds the class once per
    /// test method, so each gets its own — which keeps the project ids and
    /// document names below unique in a database every test in the collection
    /// shares.
    /// </summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    public DesignDocumentRepositoryDatabaseTests(DesignDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private Guid ProjectId => Guid.Parse($"{_run}-1111-4111-8111-111111111111");

    private string DocName(string suffix) => $"{DesignDatabaseFixture.TestDocumentPrefix}{_run}-{suffix}";

    [Fact]
    public async Task First_upload_creates_the_document_at_version_one()
    {
        var result = await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));

        Assert.Equal(1, result.Version.VersionNumber);
        Assert.True(result.DocumentWasCreated);
        Assert.Equal(ProjectId, result.Document.ProjectId);
        Assert.Equal(DesignDocumentStatus.Submitted, result.Version.Status);
    }

    [Fact]
    public async Task A_second_upload_under_the_same_name_is_version_two_of_the_same_document()
    {
        var name = DocName("plan");

        var first = await AddVersion(Upload(name, "%PDF-one"u8.ToArray()));
        var second = await AddVersion(
            Upload(name, "%PDF-two"u8.ToArray(), revisionComment: "Moved the stairs"));

        Assert.Equal(first.Document.Id, second.Document.Id);
        Assert.Equal(2, second.Version.VersionNumber);
        Assert.False(second.DocumentWasCreated);
        Assert.Equal("Moved the stairs", second.Version.RevisionComment);
    }

    [Fact]
    public async Task A_different_name_in_the_same_project_starts_a_new_document_at_version_one()
    {
        await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));
        var elevations = await AddVersion(Upload(DocName("elevations"), "%PDF-"u8.ToArray()));

        Assert.Equal(1, elevations.Version.VersionNumber);
        Assert.True(elevations.DocumentWasCreated);
    }

    [Fact]
    public async Task Lists_a_projects_documents_each_with_its_versions_oldest_first()
    {
        var plan = DocName("plan");
        await AddVersion(Upload(plan, "%PDF-v1"u8.ToArray()));
        await AddVersion(Upload(plan, "%PDF-v2"u8.ToArray()));
        await AddVersion(Upload(DocName("elevations"), "%PDF-"u8.ToArray()));

        var documents = await _fixture.Repository.ListForProjectAsync(ProjectId);

        var listed = documents.Where(d => d.Document.Name.StartsWith($"{DesignDatabaseFixture.TestDocumentPrefix}{_run}")).ToList();
        Assert.Equal(2, listed.Count);

        var planDoc = listed.Single(d => d.Document.Name == plan);
        Assert.Equal([1, 2], planDoc.Versions.Select(v => v.VersionNumber));
        // Metadata only — the bytes are not carried on a listing.
        Assert.All(planDoc.Versions, v => Assert.True(v.FileSizeBytes > 0));
    }

    [Fact]
    public async Task Reads_a_stored_file_back_byte_for_byte_with_its_project()
    {
        var bytes = "%PDF-1.7 a few bytes of body \x00\x01\x02"u8.ToArray();

        var stored = await AddVersion(Upload(DocName("plan"), bytes, fileName: "ground.pdf"));

        var file = await _fixture.Repository.GetVersionFileAsync(stored.Version.Id);

        Assert.NotNull(file);
        Assert.Equal(ProjectId, file!.ProjectId);
        Assert.Equal("ground.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(bytes, file.Content);
    }

    [Fact]
    public async Task Returns_null_for_a_version_id_that_does_not_exist()
    {
        Assert.Null(await _fixture.Repository.GetVersionFileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetVersionForReviewAsync_returns_the_version_with_its_document_and_project_context()
    {
        var stored = await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));

        var forReview = await _fixture.Repository.GetVersionForReviewAsync(stored.Version.Id);

        Assert.NotNull(forReview);
        Assert.Equal(stored.Version.Id, forReview!.VersionId);
        Assert.Equal(stored.Document.Id, forReview.DocumentId);
        Assert.Equal(ProjectId, forReview.ProjectId);
        Assert.Equal(stored.Document.Name, forReview.DocumentName);
        Assert.Equal(1, forReview.VersionNumber);
        Assert.Equal(stored.Version.UploadedBy, forReview.UploadedBy);
        Assert.Equal(DesignDocumentStatus.Submitted, forReview.Status);
    }

    [Fact]
    public async Task GetVersionForReviewAsync_returns_null_for_a_version_that_does_not_exist()
    {
        Assert.Null(await _fixture.Repository.GetVersionForReviewAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RecordReviewDecisionAsync_approves_a_submitted_version_and_writes_its_outbox_event()
    {
        var stored = await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));
        var reviewerId = Guid.Parse($"{_run}-3333-4333-8333-333333333333");
        var reviewedAt = UploadedAt.AddHours(1);

        var forReview = await _fixture.Repository.GetVersionForReviewAsync(stored.Version.Id);
        var outboxEvent = DesignEvents.Approved(forReview!, reviewerId, reviewedAt);

        var outcome = await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = stored.Version.Id,
                DocumentId = stored.Document.Id,
                Status = DesignDocumentStatus.Approved,
                ReviewedBy = reviewerId,
                ReviewedAtUtc = reviewedAt
            },
            [outboxEvent]);

        Assert.Equal(ReviewDecisionOutcome.Recorded, outcome);

        var afterwards = await _fixture.Repository.GetVersionForReviewAsync(stored.Version.Id);
        Assert.Equal(DesignDocumentStatus.Approved, afterwards!.Status);

        // The event committed in the same transaction as the decision — reading
        // it back through the outbox repository is what proves that, rather
        // than just trusting RecordReviewDecisionAsync said it did.
        var onOutbox = await _fixture.Outbox.ListForDocumentAsync(stored.Document.Id);
        Assert.Contains(onOutbox, e => e.Id == outboxEvent.Id && e.EventType == "DesignApproved");
    }

    [Fact]
    public async Task RecordReviewDecisionAsync_records_a_revision_request_with_its_comment_and_raises_no_event()
    {
        var stored = await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));
        var reviewerId = Guid.Parse($"{_run}-4444-4444-8444-444444444444");
        var reviewedAt = UploadedAt.AddHours(1);

        var outcome = await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = stored.Version.Id,
                DocumentId = stored.Document.Id,
                Status = DesignDocumentStatus.RevisionRequested,
                ReviewedBy = reviewerId,
                ReviewedAtUtc = reviewedAt,
                ReviewComment = "Move the stairs to the east wall."
            },
            []);

        Assert.Equal(ReviewDecisionOutcome.Recorded, outcome);

        var afterwards = await _fixture.Repository.GetVersionForReviewAsync(stored.Version.Id);
        Assert.Equal(DesignDocumentStatus.RevisionRequested, afterwards!.Status);

        var onOutbox = await _fixture.Outbox.ListForDocumentAsync(stored.Document.Id);
        Assert.Empty(onOutbox);
    }

    [Fact]
    public async Task RecordReviewDecisionAsync_refuses_a_version_that_is_already_decided()
    {
        var stored = await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));
        var reviewerId = Guid.Parse($"{_run}-5555-4555-8555-555555555555");

        var decision = new ReviewDecision
        {
            VersionId = stored.Version.Id,
            DocumentId = stored.Document.Id,
            Status = DesignDocumentStatus.Approved,
            ReviewedBy = reviewerId,
            ReviewedAtUtc = UploadedAt.AddHours(1)
        };

        Assert.Equal(
            ReviewDecisionOutcome.Recorded, await _fixture.Repository.RecordReviewDecisionAsync(decision, []));

        // The same decision again — as a second reviewer, or a retried request,
        // would find it.
        Assert.Equal(
            ReviewDecisionOutcome.AlreadyDecided, await _fixture.Repository.RecordReviewDecisionAsync(decision, []));
    }

    [Fact]
    public async Task RecordReviewDecisionAsync_refuses_when_another_version_of_the_document_is_already_approved()
    {
        var name = DocName("plan");
        var first = await AddVersion(Upload(name, "%PDF-one"u8.ToArray()));
        var second = await AddVersion(Upload(name, "%PDF-two"u8.ToArray()));
        var reviewerId = Guid.Parse($"{_run}-6666-4666-8666-666666666666");

        Assert.Equal(
            ReviewDecisionOutcome.Recorded,
            await _fixture.Repository.RecordReviewDecisionAsync(
                new ReviewDecision
                {
                    VersionId = first.Version.Id,
                    DocumentId = first.Document.Id,
                    Status = DesignDocumentStatus.Approved,
                    ReviewedBy = reviewerId,
                    ReviewedAtUtc = UploadedAt.AddHours(1)
                },
                []));

        // v2 is still Submitted — untouched — but v1 being Approved locks it too.
        Assert.Equal(
            ReviewDecisionOutcome.DocumentAlreadyApproved,
            await _fixture.Repository.RecordReviewDecisionAsync(
                new ReviewDecision
                {
                    VersionId = second.Version.Id,
                    DocumentId = second.Document.Id,
                    Status = DesignDocumentStatus.RevisionRequested,
                    ReviewedBy = reviewerId,
                    ReviewedAtUtc = UploadedAt.AddHours(2),
                    ReviewComment = "Too late — already approved."
                },
                []));
    }

    [Fact]
    public async Task ListForProjectAsync_reflects_a_recorded_review_decision()
    {
        // Regression: ListForProjectAsync's own SELECT once left out
        // reviewed_by/reviewed_at/review_comment, so the listing kept
        // returning null for all three even after a decision was recorded —
        // a fake repository over the same in-memory object never catches
        // this, since it never runs the SQL.
        var stored = await AddVersion(Upload(DocName("plan"), "%PDF-"u8.ToArray()));
        var reviewerId = Guid.Parse($"{_run}-7777-4777-8777-777777777777");
        var reviewedAt = UploadedAt.AddHours(1);

        await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = stored.Version.Id,
                DocumentId = stored.Document.Id,
                Status = DesignDocumentStatus.RevisionRequested,
                ReviewedBy = reviewerId,
                ReviewedAtUtc = reviewedAt,
                ReviewComment = "Move the stairs to the east wall."
            },
            []);

        var documents = await _fixture.Repository.ListForProjectAsync(ProjectId);
        var version = documents.Single(d => d.Document.Id == stored.Document.Id).Versions.Single();

        Assert.Equal(DesignDocumentStatus.RevisionRequested, version.Status);
        Assert.Equal(reviewerId, version.ReviewedBy);
        Assert.Equal(reviewedAt, version.ReviewedAt);
        Assert.Equal("Move the stairs to the east wall.", version.ReviewComment);
    }

    [Fact]
    public async Task RecordReviewDecisionAsync_returns_VersionNotFound_for_a_version_that_does_not_exist()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        var outcome = await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = versionId,
                DocumentId = documentId,
                Status = DesignDocumentStatus.Approved,
                ReviewedBy = Guid.NewGuid(),
                ReviewedAtUtc = UploadedAt
            },
            []);

        Assert.Equal(ReviewDecisionOutcome.VersionNotFound, outcome);
    }

    /// <summary>AddVersionAsync with a no-op outbox callback — see DesignSubmitted coverage in the dedicated event tests.</summary>
    private Task<DesignUploadResult> AddVersion(DesignUpload upload) =>
        _fixture.Repository.AddVersionAsync(upload, static (_, _) => []);

    private DesignUpload Upload(
        string documentName,
        byte[] content,
        string? revisionComment = null,
        string fileName = "plan.pdf",
        string contentType = "application/pdf") => new()
        {
            ProjectId = ProjectId,
            DocumentName = documentName,
            UploadedBy = Guid.Parse($"{_run}-2222-4222-8222-222222222222"),
            FileName = fileName,
            ContentType = contentType,
            Content = content,
            RevisionComment = revisionComment,
            UploadedAtUtc = UploadedAt
        };
}
