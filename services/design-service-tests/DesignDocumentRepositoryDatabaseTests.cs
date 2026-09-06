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
        var result = await _fixture.Repository.AddVersionAsync(Upload(DocName("plan"), "%PDF-"u8.ToArray()));

        Assert.Equal(1, result.Version.VersionNumber);
        Assert.True(result.DocumentWasCreated);
        Assert.Equal(ProjectId, result.Document.ProjectId);
        Assert.Equal(DesignDocumentStatus.Submitted, result.Version.Status);
    }

    [Fact]
    public async Task A_second_upload_under_the_same_name_is_version_two_of_the_same_document()
    {
        var name = DocName("plan");

        var first = await _fixture.Repository.AddVersionAsync(Upload(name, "%PDF-one"u8.ToArray()));
        var second = await _fixture.Repository.AddVersionAsync(
            Upload(name, "%PDF-two"u8.ToArray(), revisionComment: "Moved the stairs"));

        Assert.Equal(first.Document.Id, second.Document.Id);
        Assert.Equal(2, second.Version.VersionNumber);
        Assert.False(second.DocumentWasCreated);
        Assert.Equal("Moved the stairs", second.Version.RevisionComment);
    }

    [Fact]
    public async Task A_different_name_in_the_same_project_starts_a_new_document_at_version_one()
    {
        await _fixture.Repository.AddVersionAsync(Upload(DocName("plan"), "%PDF-"u8.ToArray()));
        var elevations = await _fixture.Repository.AddVersionAsync(Upload(DocName("elevations"), "%PDF-"u8.ToArray()));

        Assert.Equal(1, elevations.Version.VersionNumber);
        Assert.True(elevations.DocumentWasCreated);
    }

    [Fact]
    public async Task Lists_a_projects_documents_each_with_its_versions_oldest_first()
    {
        var plan = DocName("plan");
        await _fixture.Repository.AddVersionAsync(Upload(plan, "%PDF-v1"u8.ToArray()));
        await _fixture.Repository.AddVersionAsync(Upload(plan, "%PDF-v2"u8.ToArray()));
        await _fixture.Repository.AddVersionAsync(Upload(DocName("elevations"), "%PDF-"u8.ToArray()));

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

        var stored = await _fixture.Repository.AddVersionAsync(Upload(DocName("plan"), bytes, fileName: "ground.pdf"));

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
