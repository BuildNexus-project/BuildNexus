using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The role-dashboard design queries (US-21) run against real MySQL: "latest
/// version of each document", the review-state counts and the pending-revision
/// list are all SQL, so the SQL is what has to be shown to work.
/// </summary>
/// <remarks>
/// Needs <c>design-db</c> running — see <see cref="DesignDatabaseFixture"/>. Every
/// query is narrowed by project ids unique to the test, so whatever else the
/// development database holds cannot disturb the result.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(DesignDatabaseCollection.Name)]
public class DesignDashboardRepositoryDatabaseTests
{
    private static readonly DateTime FirstUpload = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly DesignDatabaseFixture _fixture;

    /// <summary>Unique to this test method, so its projects don't collide with any other test's in the shared database.</summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    public DesignDashboardRepositoryDatabaseTests(DesignDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private Guid ProjectA => Guid.Parse($"{_run}-1111-4111-8111-11111111111a");

    private Guid ProjectB => Guid.Parse($"{_run}-1111-4111-8111-11111111111b");

    private Guid Architect => Guid.Parse($"{_run}-2222-4222-8222-222222222222");

    private Guid Client => Guid.Parse($"{_run}-3333-4333-8333-333333333333");

    // ------------------------------------------------ the review-state tally ----

    [Fact]
    public async Task A_freshly_uploaded_document_is_awaiting_review()
    {
        await AddVersion(ProjectA, "plan", FirstUpload);

        var tally = Assert.Single(await TalliesAsync(ProjectA));

        Assert.Equal(1, tally.DocumentCount);
        Assert.Equal(1, tally.AwaitingReviewCount);
        Assert.Equal(ProjectDesignState.AwaitingReview, tally.State);
    }

    [Fact]
    public async Task An_approved_document_is_approved()
    {
        var plan = await AddVersion(ProjectA, "plan", FirstUpload);
        await ApproveAsync(plan, FirstUpload.AddHours(1));

        var tally = Assert.Single(await TalliesAsync(ProjectA));

        Assert.Equal(1, tally.ApprovedCount);
        Assert.Equal(0, tally.AwaitingReviewCount);
        Assert.Equal(ProjectDesignState.Approved, tally.State);
    }

    [Fact]
    public async Task A_document_the_client_sent_back_is_a_revision_requested()
    {
        var plan = await AddVersion(ProjectA, "plan", FirstUpload);
        await RequestRevisionAsync(plan, FirstUpload.AddHours(1), "Move the stairs");

        var tally = Assert.Single(await TalliesAsync(ProjectA));

        Assert.Equal(1, tally.RevisionRequestedCount);
        Assert.Equal(ProjectDesignState.RevisionRequested, tally.State);
    }

    [Fact]
    public async Task Uploading_a_newer_version_answers_the_revision_and_only_the_latest_version_counts()
    {
        // v1 was sent back; v2 is the Architect's answer and is waiting on review.
        // Counting every version would report a revision still outstanding.
        var v1 = await AddVersion(ProjectA, "plan", FirstUpload);
        await RequestRevisionAsync(v1, FirstUpload.AddHours(1), "Move the stairs");
        await AddVersion(ProjectA, "plan", FirstUpload.AddHours(2));

        var tally = Assert.Single(await TalliesAsync(ProjectA));

        Assert.Equal(1, tally.DocumentCount);
        Assert.Equal(1, tally.AwaitingReviewCount);
        Assert.Equal(0, tally.RevisionRequestedCount);
        Assert.Equal(ProjectDesignState.AwaitingReview, tally.State);
    }

    [Fact]
    public async Task A_project_with_several_documents_is_tallied_across_all_of_them()
    {
        var plan = await AddVersion(ProjectA, "plan", FirstUpload);
        await ApproveAsync(plan, FirstUpload.AddHours(1));
        var elevations = await AddVersion(ProjectA, "elevations", FirstUpload);
        await RequestRevisionAsync(elevations, FirstUpload.AddHours(1), "Taller windows");
        await AddVersion(ProjectA, "sections", FirstUpload);

        var tally = Assert.Single(await TalliesAsync(ProjectA));

        Assert.Equal(3, tally.DocumentCount);
        Assert.Equal(1, tally.ApprovedCount);
        Assert.Equal(1, tally.RevisionRequestedCount);
        Assert.Equal(1, tally.AwaitingReviewCount);
        Assert.Equal(tally.DocumentCount, tally.ApprovedCount + tally.RevisionRequestedCount + tally.AwaitingReviewCount);
        // Something waiting on the Client outranks a revision they are waiting on.
        Assert.Equal(ProjectDesignState.AwaitingReview, tally.State);
    }

    [Fact]
    public async Task Only_the_projects_asked_about_are_tallied()
    {
        await AddVersion(ProjectA, "plan", FirstUpload);
        await AddVersion(ProjectB, "plan", FirstUpload);

        var tallies = await TalliesAsync(ProjectA);

        Assert.Equal([ProjectA], tallies.Select(t => t.ProjectId));
    }

    [Fact]
    public async Task A_project_with_no_documents_is_left_out_rather_than_reported_as_empty()
    {
        await AddVersion(ProjectA, "plan", FirstUpload);

        var tallies = await TalliesAsync(ProjectA, ProjectB);

        Assert.Equal([ProjectA], tallies.Select(t => t.ProjectId));
    }

    [Fact]
    public async Task Asking_about_no_projects_answers_nothing_without_error()
    {
        Assert.Empty(await _fixture.Dashboard.GetDesignTalliesAsync([]));
        Assert.Empty(await _fixture.Dashboard.ListPendingRevisionsAsync([]));
    }

    // ------------------------------------------------- the pending revisions ----

    [Fact]
    public async Task A_pending_revision_carries_the_document_the_version_and_what_the_client_asked_for()
    {
        var plan = await AddVersion(ProjectA, "plan", FirstUpload);
        await RequestRevisionAsync(plan, FirstUpload.AddHours(3), "Move the stairs");

        var revision = Assert.Single(await RevisionsAsync(ProjectA));

        Assert.Equal(ProjectA, revision.ProjectId);
        Assert.Equal(plan.Document.Id, revision.DocumentId);
        Assert.Equal($"{DesignDatabaseFixture.TestDocumentPrefix}{_run}-plan", revision.DocumentName);
        Assert.Equal(1, revision.VersionNumber);
        Assert.Equal("Move the stairs", revision.ReviewComment);
        Assert.Equal(FirstUpload.AddHours(3), revision.RequestedAtUtc);
    }

    [Fact]
    public async Task A_revision_the_architect_has_answered_is_no_longer_pending()
    {
        var v1 = await AddVersion(ProjectA, "plan", FirstUpload);
        await RequestRevisionAsync(v1, FirstUpload.AddHours(1), "Move the stairs");
        await AddVersion(ProjectA, "plan", FirstUpload.AddHours(2));

        Assert.Empty(await RevisionsAsync(ProjectA));
    }

    [Fact]
    public async Task Documents_that_are_submitted_or_approved_are_not_pending_revisions()
    {
        await AddVersion(ProjectA, "waiting", FirstUpload);
        var signedOff = await AddVersion(ProjectA, "signed-off", FirstUpload);
        await ApproveAsync(signedOff, FirstUpload.AddHours(1));

        Assert.Empty(await RevisionsAsync(ProjectA));
    }

    [Fact]
    public async Task Pending_revisions_are_scoped_to_the_projects_asked_about()
    {
        var mine = await AddVersion(ProjectA, "plan", FirstUpload);
        await RequestRevisionAsync(mine, FirstUpload.AddHours(1), "Move the stairs");
        var theirs = await AddVersion(ProjectB, "plan", FirstUpload);
        await RequestRevisionAsync(theirs, FirstUpload.AddHours(1), "Add a window");

        var revisions = await RevisionsAsync(ProjectA);

        Assert.Equal([ProjectA], revisions.Select(r => r.ProjectId));
    }

    [Fact]
    public async Task Pending_revisions_come_back_longest_waiting_first()
    {
        var recent = await AddVersion(ProjectA, "recent", FirstUpload);
        var oldest = await AddVersion(ProjectB, "oldest", FirstUpload);
        var middle = await AddVersion(ProjectA, "middle", FirstUpload);

        await RequestRevisionAsync(recent, FirstUpload.AddHours(9), "c");
        await RequestRevisionAsync(oldest, FirstUpload.AddHours(1), "a");
        await RequestRevisionAsync(middle, FirstUpload.AddHours(5), "b");

        var revisions = await RevisionsAsync(ProjectA, ProjectB);

        Assert.Equal(["a", "b", "c"], revisions.Select(r => r.ReviewComment));
    }

    // ------------------------------------------------------------ helpers ----

    private async Task<IReadOnlyList<ProjectDesignTally>> TalliesAsync(params Guid[] projectIds) =>
        await _fixture.Dashboard.GetDesignTalliesAsync(projectIds);

    private async Task<IReadOnlyList<PendingRevision>> RevisionsAsync(params Guid[] projectIds) =>
        await _fixture.Dashboard.ListPendingRevisionsAsync(projectIds);

    private Task<DesignUploadResult> AddVersion(Guid projectId, string name, DateTime uploadedAtUtc) =>
        _fixture.Repository.AddVersionAsync(
            new DesignUpload
            {
                ProjectId = projectId,
                DocumentName = $"{DesignDatabaseFixture.TestDocumentPrefix}{_run}-{name}",
                UploadedBy = Architect,
                FileName = "plan.pdf",
                ContentType = "application/pdf",
                Content = "%PDF-1.4"u8.ToArray(),
                RevisionComment = null,
                UploadedAtUtc = uploadedAtUtc
            },
            static (_, _) => []);

    private async Task ApproveAsync(DesignUploadResult upload, DateTime reviewedAtUtc)
    {
        var outcome = await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = upload.Version.Id,
                DocumentId = upload.Document.Id,
                Status = DesignDocumentStatus.Approved,
                ReviewedBy = Client,
                ReviewedAtUtc = reviewedAtUtc
            },
            []);

        Assert.Equal(ReviewDecisionOutcome.Recorded, outcome);
    }

    private async Task RequestRevisionAsync(DesignUploadResult upload, DateTime reviewedAtUtc, string comment)
    {
        var outcome = await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = upload.Version.Id,
                DocumentId = upload.Document.Id,
                Status = DesignDocumentStatus.RevisionRequested,
                ReviewedBy = Client,
                ReviewedAtUtc = reviewedAtUtc,
                ReviewComment = comment
            },
            []);

        Assert.Equal(ReviewDecisionOutcome.Recorded, outcome);
    }
}
