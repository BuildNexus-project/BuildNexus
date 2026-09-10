using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The design approval report query (US-20) run against real MySQL — the CTE,
/// the per-project rollup, and the way AVG() skips the documents that are not
/// approved yet.
/// </summary>
/// <remarks>Needs <c>design-db</c> running — see <see cref="DesignDatabaseFixture"/>.</remarks>
[Collection(DesignDatabaseCollection.Name)]
public class DesignReportRepositoryDatabaseTests
{
    private static readonly DateTime FirstUpload = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly DesignDatabaseFixture _fixture;

    /// <summary>Unique to this test method, so its projects don't collide with any other test's in the shared database.</summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    public DesignReportRepositoryDatabaseTests(DesignDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private Guid ProjectA => Guid.Parse($"{_run}-1111-4111-8111-11111111111a");

    private Guid ProjectB => Guid.Parse($"{_run}-1111-4111-8111-11111111111b");

    private Guid Architect => Guid.Parse($"{_run}-2222-4222-8222-222222222222");

    [Fact]
    public async Task Aggregates_version_count_and_time_to_approval_per_project()
    {
        // Project A: one document, three uploads, approved on v3 five hours
        // after the first upload.
        var docA = await AddVersion(Upload(ProjectA, "plan", FirstUpload));
        await AddVersion(Upload(ProjectA, "plan", FirstUpload.AddHours(1)));
        var v3 = await AddVersion(Upload(ProjectA, "plan", FirstUpload.AddHours(2)));

        await _fixture.Repository.RecordReviewDecisionAsync(
            new ReviewDecision
            {
                VersionId = v3.Version.Id,
                DocumentId = docA.Document.Id,
                Status = DesignDocumentStatus.Approved,
                ReviewedBy = Architect,
                ReviewedAtUtc = FirstUpload.AddHours(5)
            },
            []);

        // Project B: one document, two uploads, nothing approved.
        await AddVersion(Upload(ProjectB, "plan", FirstUpload));
        await AddVersion(Upload(ProjectB, "plan", FirstUpload.AddHours(1)));

        var report = await _fixture.Reports.GetApprovalReportAsync();

        var a = report.Single(r => r.ProjectId == ProjectA);
        Assert.Equal(1, a.DocumentCount);
        Assert.Equal(1, a.ApprovedDocumentCount);
        Assert.Equal(3, a.TotalVersionCount);
        Assert.Equal(3.0, a.AverageVersionsToApproval);
        Assert.NotNull(a.AverageHoursToApproval);
        Assert.Equal(5.0, a.AverageHoursToApproval!.Value, precision: 3);

        var b = report.Single(r => r.ProjectId == ProjectB);
        Assert.Equal(1, b.DocumentCount);
        Assert.Equal(0, b.ApprovedDocumentCount);
        Assert.Equal(2, b.TotalVersionCount);
        Assert.Null(b.AverageVersionsToApproval);
        Assert.Null(b.AverageHoursToApproval);
    }

    [Fact]
    public async Task Averages_across_a_projects_documents_and_counts_every_version()
    {
        // One project, two documents: "plan" approved on v2, "elevations"
        // approved on v4 — average versions-to-approval is (2 + 4) / 2 = 3,
        // and total versions is every row, 2 + 4 = 6.
        var plan = await AddVersion(Upload(ProjectA, "plan", FirstUpload));
        var planV2 = await AddVersion(Upload(ProjectA, "plan", FirstUpload.AddHours(1)));
        await _fixture.Repository.RecordReviewDecisionAsync(
            Approve(planV2.Version.Id, plan.Document.Id, FirstUpload.AddHours(2)), []);

        var elevations = await AddVersion(Upload(ProjectA, "elevations", FirstUpload));
        await AddVersion(Upload(ProjectA, "elevations", FirstUpload.AddHours(1)));
        await AddVersion(Upload(ProjectA, "elevations", FirstUpload.AddHours(2)));
        var elevV4 = await AddVersion(Upload(ProjectA, "elevations", FirstUpload.AddHours(3)));
        await _fixture.Repository.RecordReviewDecisionAsync(
            Approve(elevV4.Version.Id, elevations.Document.Id, FirstUpload.AddHours(10)), []);

        var row = (await _fixture.Reports.GetApprovalReportAsync()).Single(r => r.ProjectId == ProjectA);

        Assert.Equal(2, row.DocumentCount);
        Assert.Equal(2, row.ApprovedDocumentCount);
        Assert.Equal(6, row.TotalVersionCount);
        Assert.Equal(3.0, row.AverageVersionsToApproval);
        // "plan" took 2h, "elevations" took 10h — mean 6h.
        Assert.Equal(6.0, row.AverageHoursToApproval!.Value, precision: 3);
    }

    [Fact]
    public async Task Orders_rows_by_project_id_not_by_when_the_work_happened()
    {
        // B's design is uploaded first, but A sorts before B.
        await AddVersion(Upload(ProjectB, "plan", FirstUpload));
        await AddVersion(Upload(ProjectA, "plan", FirstUpload.AddHours(1)));

        var mine = (await _fixture.Reports.GetApprovalReportAsync())
            .Where(r => r.ProjectId == ProjectA || r.ProjectId == ProjectB)
            .Select(r => r.ProjectId)
            .ToList();

        Assert.Equal([ProjectA, ProjectB], mine);
    }

    /// <summary>AddVersionAsync with a no-op outbox callback — these tests don't assert on the DesignSubmitted event.</summary>
    private Task<DesignUploadResult> AddVersion(DesignUpload upload) =>
        _fixture.Repository.AddVersionAsync(upload, static (_, _) => []);

    private ReviewDecision Approve(Guid versionId, Guid documentId, DateTime reviewedAtUtc) => new()
    {
        VersionId = versionId,
        DocumentId = documentId,
        Status = DesignDocumentStatus.Approved,
        ReviewedBy = Architect,
        ReviewedAtUtc = reviewedAtUtc
    };

    private DesignUpload Upload(Guid projectId, string name, DateTime uploadedAtUtc) => new()
    {
        ProjectId = projectId,
        DocumentName = $"{DesignDatabaseFixture.TestDocumentPrefix}{_run}-{name}",
        UploadedBy = Architect,
        FileName = "plan.pdf",
        ContentType = "application/pdf",
        Content = "%PDF-1.4"u8.ToArray(),
        RevisionComment = null,
        UploadedAtUtc = uploadedAtUtc
    };
}
