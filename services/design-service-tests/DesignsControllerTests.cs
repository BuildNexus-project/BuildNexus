using System.Security.Claims;
using BuildNexus.DesignService.Configuration;
using BuildNexus.DesignService.Contracts;
using BuildNexus.DesignService.Controllers;
using BuildNexus.DesignService.Models;
using BuildNexus.DesignService.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The US-09 endpoints walked over stand-in collaborators: what the upload
/// stores and where each field comes from, how the per-project answer from the
/// Project Service is relayed, and how a bad file is refused. No MySQL, no
/// Project Service — the repository and the access client are fakes.
/// </summary>
public class DesignsControllerTests
{
    private static readonly Guid ArchitectId = Guid.Parse("a11ce000-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("c11e0000-0000-4000-8000-000000000002");
    private static readonly Guid ProjectId = Guid.Parse("9f01d000-0000-4000-8000-000000000009");
    private const string Token = "forwarded.access.token";

    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    [Fact]
    public async Task Upload_stores_the_version_and_answers_201_with_its_metadata()
    {
        var (controller, repository, _) = ControllerFor();

        var result = Assert.IsType<ObjectResult>(
            await controller.Upload(ProjectId, Request(name: "GroundFloorPlan"), default));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);

        var body = Assert.IsType<DesignVersionResponse>(result.Value);
        Assert.Equal("GroundFloorPlan_v1", body.DisplayName);
        Assert.Equal("Submitted", body.Status);
        Assert.Equal(ProjectId, body.ProjectId);
        Assert.Equal(1, body.VersionNumber);
        // A fresh upload is Submitted, and Submitted is never current.
        Assert.False(body.IsCurrent);

        Assert.NotNull(repository.LastUpload);
        Assert.Equal(ProjectId, repository.LastUpload!.ProjectId);
        // The uploader is the token's subject, never anything on the form.
        Assert.Equal(ArchitectId, repository.LastUpload.UploadedBy);
        Assert.Equal("GroundFloorPlan", repository.LastUpload.DocumentName);
    }

    [Fact]
    public async Task Upload_takes_the_content_type_from_the_bytes_not_the_form()
    {
        var (controller, repository, _) = ControllerFor();

        // PNG bytes, but the part is named "plan.pdf" and could claim any type.
        await controller.Upload(ProjectId, Request(bytes: Png, fileName: "plan.pdf"), default);

        Assert.Equal("image/png", repository.LastUpload!.ContentType);
    }

    [Fact]
    public async Task Upload_trims_the_name_and_stores_a_blank_comment_as_null()
    {
        var (controller, repository, _) = ControllerFor();

        await controller.Upload(ProjectId, Request(name: "  Elevations  ", revisionComment: "   "), default);

        Assert.Equal("Elevations", repository.LastUpload!.DocumentName);
        Assert.Null(repository.LastUpload.RevisionComment);
    }

    [Fact]
    public async Task Upload_forwards_the_callers_token_and_the_route_project_to_the_project_service()
    {
        var (controller, _, access) = ControllerFor();

        await controller.Upload(ProjectId, Request(), default);

        Assert.Equal(ProjectId, access.LastProjectId);
        Assert.Equal(Token, access.LastToken);
    }

    [Fact]
    public async Task Upload_is_404_when_the_project_does_not_exist()
    {
        var (controller, repository, _) = ControllerFor(access: ProjectAccess.NotFound);

        var result = Assert.IsType<ObjectResult>(await controller.Upload(ProjectId, Request(), default));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Null(repository.LastUpload);
    }

    [Fact]
    public async Task Upload_is_403_when_the_caller_is_not_on_the_project()
    {
        var (controller, repository, _) = ControllerFor(access: ProjectAccess.Forbidden);

        var result = Assert.IsType<ObjectResult>(await controller.Upload(ProjectId, Request(), default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Null(repository.LastUpload);
    }

    [Fact]
    public async Task Upload_is_502_when_the_project_service_cannot_be_reached()
    {
        var (controller, repository, _) = ControllerFor(access: ProjectAccess.Unavailable);

        var result = Assert.IsType<ObjectResult>(await controller.Upload(ProjectId, Request(), default));

        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Null(repository.LastUpload);
    }

    [Fact]
    public async Task Upload_rejects_a_file_that_is_not_pdf_jpg_or_png()
    {
        var (controller, repository, _) = ControllerFor();

        var result = Assert.IsType<ObjectResult>(
            await controller.Upload(ProjectId, Request(bytes: "plain text"u8.ToArray()), default));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(DesignFileValidator.UnsupportedFormatMessage, ((ProblemDetails)result.Value!).Detail);
        Assert.Null(repository.LastUpload);
    }

    [Fact]
    public async Task Upload_rejects_an_empty_file()
    {
        var (controller, _, _) = ControllerFor();

        var result = Assert.IsType<ObjectResult>(
            await controller.Upload(ProjectId, Request(bytes: []), default));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(DesignFileValidator.EmptyFileMessage, ((ProblemDetails)result.Value!).Detail);
    }

    [Fact]
    public async Task Upload_rejects_an_oversized_file_from_its_length_without_reading_it()
    {
        var (controller, repository, access) = ControllerFor();

        // A part that reports a length over the limit but is not actually that
        // big — the check is on Length, before the bytes are read.
        var request = new UploadDesignDocumentRequest
        {
            Name = "Big",
            File = new FormFile(new MemoryStream([1, 2, 3]), 0, DesignFileValidator.MaxFileSizeBytes + 1, "file", "big.pdf")
        };

        var result = Assert.IsType<ObjectResult>(await controller.Upload(ProjectId, request, default));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(DesignFileValidator.TooLargeMessage, ((ProblemDetails)result.Value!).Detail);
        Assert.Null(repository.LastUpload);
    }

    [Fact]
    public async Task Upload_refuses_a_token_with_no_usable_subject()
    {
        var (controller, repository, access) = ControllerFor(subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.Upload(ProjectId, Request(), default));

        Assert.Null(repository.LastUpload);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task List_returns_the_projects_documents_for_an_allowed_caller()
    {
        var (controller, repository, _) = ControllerFor();
        repository.Documents.Add(DocumentWithVersions(
            "GroundFloorPlan", DesignDocumentStatus.Submitted, DesignDocumentStatus.Submitted));

        var result = Assert.IsType<OkObjectResult>(await controller.ListForProject(ProjectId, default));

        var documents = Assert.IsAssignableFrom<IEnumerable<DesignDocumentResponse>>(result.Value).ToList();
        Assert.Single(documents);
        Assert.Equal("GroundFloorPlan", documents[0].Name);
        Assert.Equal(["GroundFloorPlan_v1", "GroundFloorPlan_v2"], documents[0].Versions.Select(v => v.DisplayName));
        Assert.Equal(2, documents[0].LatestVersionNumber);
    }

    [Fact]
    public async Task List_marks_no_version_current_while_none_has_been_reviewed()
    {
        var (controller, repository, _) = ControllerFor();
        repository.Documents.Add(DocumentWithVersions(
            "GroundFloorPlan", DesignDocumentStatus.Submitted, DesignDocumentStatus.Submitted));

        var result = Assert.IsType<OkObjectResult>(await controller.ListForProject(ProjectId, default));

        var versions = Assert.IsAssignableFrom<IEnumerable<DesignDocumentResponse>>(result.Value).Single().Versions;
        Assert.All(versions, version => Assert.False(version.IsCurrent));
    }

    [Fact]
    public async Task List_marks_the_highest_numbered_approved_or_under_review_version_current()
    {
        var (controller, repository, _) = ControllerFor();
        // v2 is Approved and comes first, but v4 is UnderReview and has the
        // higher number — v4 is current, not v2, and neither Submitted version
        // (v1, v3) ever qualifies.
        repository.Documents.Add(DocumentWithVersions(
            "GroundFloorPlan",
            DesignDocumentStatus.Submitted,
            DesignDocumentStatus.Approved,
            DesignDocumentStatus.Submitted,
            DesignDocumentStatus.UnderReview));

        var result = Assert.IsType<OkObjectResult>(await controller.ListForProject(ProjectId, default));

        var versions = Assert.IsAssignableFrom<IEnumerable<DesignDocumentResponse>>(result.Value)
            .Single().Versions.ToDictionary(v => v.VersionNumber, v => v.IsCurrent);

        Assert.Equal(new Dictionary<int, bool> { [1] = false, [2] = false, [3] = false, [4] = true }, versions);
    }

    [Theory]
    [InlineData(ProjectAccessOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ProjectAccessOutcome.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ProjectAccessOutcome.Unavailable, StatusCodes.Status502BadGateway)]
    public async Task List_relays_the_project_service_refusal(ProjectAccessOutcome outcome, int expectedStatus)
    {
        var (controller, _, _) = ControllerFor(access: AccessFor(outcome));

        var result = Assert.IsType<ObjectResult>(await controller.ListForProject(ProjectId, default));

        Assert.Equal(expectedStatus, result.StatusCode);
    }

    [Fact]
    public async Task Download_streams_the_stored_bytes_for_an_allowed_caller()
    {
        var (controller, repository, _) = ControllerFor();
        var versionId = Guid.NewGuid();
        repository.StoredFile = new StoredDesignFile
        {
            VersionId = versionId,
            ProjectId = ProjectId,
            FileName = "ground.pdf",
            ContentType = "application/pdf",
            Content = Pdf
        };

        var result = Assert.IsType<FileContentResult>(await controller.DownloadVersionFile(versionId, default));

        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("ground.pdf", result.FileDownloadName);
        Assert.Equal(Pdf, result.FileContents);
    }

    [Fact]
    public async Task Download_is_404_for_a_version_that_does_not_exist_and_never_asks_the_project_service()
    {
        var (controller, _, access) = ControllerFor();

        var result = Assert.IsType<ObjectResult>(await controller.DownloadVersionFile(Guid.NewGuid(), default));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task Download_is_403_when_the_caller_is_not_on_the_versions_project()
    {
        var (controller, repository, _) = ControllerFor(access: ProjectAccess.Forbidden);
        var versionId = Guid.NewGuid();
        repository.StoredFile = new StoredDesignFile
        {
            VersionId = versionId,
            ProjectId = ProjectId,
            FileName = "ground.pdf",
            ContentType = "application/pdf",
            Content = Pdf
        };

        var result = Assert.IsType<ObjectResult>(await controller.DownloadVersionFile(versionId, default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Approve_records_the_decision_enqueues_its_event_and_answers_200()
    {
        var (controller, repository, notifier) = ControllerForReview();
        var document = DocumentWithVersions("GroundFloorPlan", DesignDocumentStatus.Submitted);
        repository.Documents.Add(document);
        var versionId = document.Versions[0].Id;

        var result = Assert.IsType<OkObjectResult>(await controller.Approve(versionId, default));

        var body = Assert.IsType<ReviewDecisionResponse>(result.Value);
        Assert.Equal("Approved", body.Status);
        Assert.Equal(ClientId, body.ReviewedBy);
        Assert.Null(body.ReviewComment);

        Assert.NotNull(repository.LastReviewDecision);
        Assert.Equal(DesignDocumentStatus.Approved, repository.LastReviewDecision!.Status);
        var enqueued = Assert.Single(repository.LastOutboxEvents);
        Assert.Equal("DesignApproved", enqueued.EventType);

        // Approval notifies nobody — only a revision request does.
        Assert.Null(notifier.LastNotification);
    }

    [Fact]
    public async Task Approve_is_404_for_a_version_that_does_not_exist()
    {
        var (controller, repository, _) = ControllerForReview();

        var result = Assert.IsType<ObjectResult>(await controller.Approve(Guid.NewGuid(), default));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Null(repository.LastReviewDecision);
    }

    [Fact]
    public async Task Approve_is_403_when_the_caller_is_not_on_the_project()
    {
        var (controller, repository, _) = ControllerForReview(access: ProjectAccess.Forbidden);
        var document = DocumentWithVersions("GroundFloorPlan", DesignDocumentStatus.Submitted);
        repository.Documents.Add(document);

        var result = Assert.IsType<ObjectResult>(await controller.Approve(document.Versions[0].Id, default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Null(repository.LastReviewDecision);
    }

    [Fact]
    public async Task Approve_is_409_when_the_version_was_already_decided()
    {
        var (controller, repository, _) = ControllerForReview();
        var document = DocumentWithVersions("GroundFloorPlan", DesignDocumentStatus.RevisionRequested);
        repository.Documents.Add(document);

        var result = Assert.IsType<ObjectResult>(await controller.Approve(document.Versions[0].Id, default));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Approve_is_409_when_another_version_of_the_document_is_already_approved()
    {
        var (controller, repository, _) = ControllerForReview();
        // v1 Approved, v2 still Submitted — v1 being Approved locks v2 too.
        var document = DocumentWithVersions(
            "GroundFloorPlan", DesignDocumentStatus.Approved, DesignDocumentStatus.Submitted);
        repository.Documents.Add(document);

        var result = Assert.IsType<ObjectResult>(await controller.Approve(document.Versions[1].Id, default));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task RequestRevision_records_the_comment_notifies_the_architect_and_raises_a_DesignRevisionRequested_event()
    {
        var (controller, repository, notifier) = ControllerForReview();
        var document = DocumentWithVersions("GroundFloorPlan", DesignDocumentStatus.Submitted);
        repository.Documents.Add(document);
        var versionId = document.Versions[0].Id;

        var result = Assert.IsType<OkObjectResult>(
            await controller.RequestRevision(
                versionId, new RequestRevisionRequest { Comment = "Move the stairs to the east wall." }, default));

        var body = Assert.IsType<ReviewDecisionResponse>(result.Value);
        Assert.Equal("RevisionRequested", body.Status);
        Assert.Equal("Move the stairs to the east wall.", body.ReviewComment);

        // US-23: a revision request now raises its own event, in the same
        // transaction as the decision.
        var enqueued = Assert.Single(repository.LastOutboxEvents);
        Assert.Equal("DesignRevisionRequested", enqueued.EventType);

        Assert.NotNull(notifier.LastNotification);
        Assert.Equal(ArchitectId, notifier.LastNotification!.Value.ArchitectId);
        Assert.Equal("Move the stairs to the east wall.", notifier.LastNotification.Value.Comment);
    }

    [Fact]
    public async Task RequestRevision_still_answers_200_when_the_notification_fails()
    {
        var notifier = new FakeRevisionRequestNotifier { ThrowOnNotify = new InvalidOperationException("Mail server unreachable.") };
        var (controller, repository, _) = ControllerForReview(notifier: notifier);
        var document = DocumentWithVersions("GroundFloorPlan", DesignDocumentStatus.Submitted);
        repository.Documents.Add(document);

        // The decision is already recorded by the time the notifier is asked —
        // a mail server being down must not turn that into a failed request.
        var result = Assert.IsType<OkObjectResult>(
            await controller.RequestRevision(
                document.Versions[0].Id, new RequestRevisionRequest { Comment = "Fix the roof line." }, default));

        Assert.Equal("RevisionRequested", Assert.IsType<ReviewDecisionResponse>(result.Value).Status);
    }

    [Fact]
    public async Task RequestRevision_is_409_when_the_document_already_has_an_approved_version()
    {
        var (controller, repository, _) = ControllerForReview();
        var document = DocumentWithVersions(
            "GroundFloorPlan", DesignDocumentStatus.Approved, DesignDocumentStatus.Submitted);
        repository.Documents.Add(document);

        var result = Assert.IsType<ObjectResult>(
            await controller.RequestRevision(
                document.Versions[1].Id, new RequestRevisionRequest { Comment = "Too late." }, default));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    /// <summary>
    /// Like <see cref="ControllerFor"/>, but signed in as a Client — the only
    /// role <see cref="DesignsController.Approve"/> and
    /// <see cref="DesignsController.RequestRevision"/> accept — and with a
    /// <see cref="FakeRevisionRequestNotifier"/> exposed for assertion.
    /// </summary>
    private static (DesignsController Controller, FakeDesignDocumentRepository Repository, FakeRevisionRequestNotifier Notifier)
        ControllerForReview(ProjectAccess? access = null, FakeRevisionRequestNotifier? notifier = null)
    {
        var repository = new FakeDesignDocumentRepository();
        var accessClient = new FakeProjectAccessClient { Result = access ?? ProjectAccess.Allowed(null) };
        var fakeNotifier = notifier ?? new FakeRevisionRequestNotifier();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, ClientId.ToString()),
                new Claim(JwtOptions.RoleClaimType, "Client")
            ], "TestAuth"))
        };
        httpContext.Request.Headers.Authorization = $"Bearer {Token}";

        var controller = new DesignsController(
            repository, accessClient, fakeNotifier, NullLogger<DesignsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, repository, fakeNotifier);
    }

    private static (DesignsController Controller, FakeDesignDocumentRepository Repository, FakeProjectAccessClient Access)
        ControllerFor(ProjectAccess? access = null, string? subject = null)
    {
        var repository = new FakeDesignDocumentRepository();
        var accessClient = new FakeProjectAccessClient { Result = access ?? ProjectAccess.Allowed(null) };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject ?? ArchitectId.ToString()),
                new Claim(JwtOptions.RoleClaimType, "Architect")
            ], "TestAuth"))
        };
        httpContext.Request.Headers.Authorization = $"Bearer {Token}";

        var controller = new DesignsController(
            repository, accessClient, new FakeRevisionRequestNotifier(), NullLogger<DesignsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, repository, accessClient);
    }

    private static ProjectAccess AccessFor(ProjectAccessOutcome outcome) => outcome switch
    {
        ProjectAccessOutcome.NotFound => ProjectAccess.NotFound,
        ProjectAccessOutcome.Forbidden => ProjectAccess.Forbidden,
        ProjectAccessOutcome.Unavailable => ProjectAccess.Unavailable,
        _ => ProjectAccess.Allowed(null)
    };

    private static UploadDesignDocumentRequest Request(
        string name = "GroundFloorPlan",
        string? revisionComment = null,
        byte[]? bytes = null,
        string fileName = "plan.pdf")
    {
        var content = bytes ?? Pdf;

        return new UploadDesignDocumentRequest
        {
            Name = name,
            RevisionComment = revisionComment,
            File = new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName)
        };
    }

    /// <summary>One version per status given, numbered 1, 2, 3... in that order.</summary>
    private static DesignDocumentWithVersions DocumentWithVersions(string name, params DesignDocumentStatus[] statuses)
    {
        var documentId = Guid.NewGuid();
        var document = new DesignDocument
        {
            Id = documentId,
            ProjectId = ProjectId,
            Name = name,
            CreatedBy = ArchitectId,
            CreatedAt = new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc)
        };

        var versionRows = statuses.Select((status, index) =>
        {
            var n = index + 1;

            return new DesignDocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                VersionNumber = n,
                FileName = "plan.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 8,
                Status = status,
                UploadedBy = ArchitectId,
                UploadedAt = document.CreatedAt.AddMinutes(n)
            };
        }).ToList();

        return new DesignDocumentWithVersions { Document = document, Versions = versionRows };
    }
}
