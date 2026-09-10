using System.Security.Claims;
using BuildNexus.DesignService.Authorization;
using BuildNexus.DesignService.Contracts;
using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Messaging;
using BuildNexus.DesignService.Models;
using BuildNexus.DesignService.Projects;
using BuildNexus.DesignService.Services;
using BuildNexus.DesignService.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.DesignService.Controllers;

/// <summary>
/// Design documents for a project. Every action requires a valid bearer token —
/// an expired, tampered or malformed token is rejected with 401 before the
/// action runs — and names the roles allowed to call it.
/// </summary>
/// <remarks>
/// The role gets a caller as far as the endpoint. Whether they may see or add
/// documents for <em>this</em> project is a per-project question this service
/// does not own: it forwards the caller's token to the Project Service's
/// <c>GET /api/projects/{id}</c> and relays that answer. A 404 there is a 404
/// here, a 403 there is a 403 here.
/// </remarks>
[ApiController]
[Route("api/designs")]
[Authorize]
public class DesignsController : ControllerBase
{
    private readonly IDesignDocumentRepository _repository;
    private readonly IProjectAccessClient _projectAccess;
    private readonly IRevisionRequestNotifier _revisionRequestNotifier;
    private readonly ILogger<DesignsController> _logger;

    public DesignsController(
        IDesignDocumentRepository repository,
        IProjectAccessClient projectAccess,
        IRevisionRequestNotifier revisionRequestNotifier,
        ILogger<DesignsController> logger)
    {
        _repository = repository;
        _projectAccess = projectAccess;
        _revisionRequestNotifier = revisionRequestNotifier;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a design document for a project. A name not seen before starts a
    /// new document at version 1; a name already there adds the next version.
    /// Allowed roles: Architect.
    /// </summary>
    /// <remarks>
    /// Architect only, and deliberately so: US-09 is an Architect submitting
    /// their work. The file must be a PDF, JPG or PNG — decided by its own
    /// bytes, not the multipart content type — and within 10 MB; anything else
    /// is refused with a message. Every version is stored with status
    /// <c>Submitted</c>, which the caller cannot set.
    /// </remarks>
    /// <response code="201">The version was stored, and is returned with its metadata.</response>
    /// <response code="400">The form failed validation, or the file was empty, too large, or not a PDF/JPG/PNG.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Architect, or is not party to this project.</response>
    /// <response code="404">No project has that id.</response>
    /// <response code="502">The Project Service could not be reached to check access.</response>
    [HttpPost("projects/{projectId:guid}/documents")]
    [Authorize(Roles = PlatformRoles.Architect)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DesignVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Upload(
        Guid projectId,
        [FromForm] UploadDesignDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var architectId) || CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        if (await CheckProjectAccessAsync(projectId, token, cancellationToken) is { } denied)
        {
            return denied;
        }

        // The length is known from the multipart headers, so an oversized upload
        // is refused before its bytes are read into memory.
        if (request.File.Length > DesignFileValidator.MaxFileSizeBytes)
        {
            return FileProblem(DesignFileValidator.TooLargeMessage);
        }

        var content = await ReadAllBytesAsync(request.File, cancellationToken);

        var validation = DesignFileValidator.Validate(content);

        if (!validation.IsValid)
        {
            return FileProblem(validation.Error!);
        }

        var stored = await _repository.AddVersionAsync(
            new DesignUpload
            {
                ProjectId = projectId,
                DocumentName = request.Name.Trim(),
                UploadedBy = architectId,
                FileName = request.File.FileName,
                // The type the bytes actually are, not what the client labelled the part.
                ContentType = validation.ContentType!,
                Content = content,
                RevisionComment = NullIfBlank(request.RevisionComment),
                UploadedAtUtc = DateTime.UtcNow
            },
            // Raised for every upload (US-23). The document and version are only
            // known once the transaction has created them.
            (document, version) => [DesignEvents.Submitted(document, version)]);

        _logger.LogInformation(
            "Architect {ArchitectId} uploaded {DisplayName} ({SizeBytes} bytes) to project {ProjectId}.",
            architectId,
            $"{stored.Document.Name}_v{stored.Version.VersionNumber}",
            stored.Version.FileSizeBytes,
            projectId);

        return StatusCode(
            StatusCodes.Status201Created,
            // Every upload lands as Submitted (see AddVersionAsync), and Submitted
            // is never current — so a just-uploaded version never is either.
            DesignVersionResponse.From(stored.Document, stored.Version, isCurrent: false));
    }

    /// <summary>
    /// Lists a project's design documents, each with every version uploaded
    /// under it. Allowed roles: Client, Architect, Project Manager, Admin.
    /// </summary>
    /// <remarks>
    /// Open to every role so the owning Client can review the latest work, with
    /// the per-project check — via the Project Service — deciding whether this
    /// caller may see this project at all.
    /// </remarks>
    /// <response code="200">The project's documents, oldest first, each with its versions oldest first.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not party to this project.</response>
    /// <response code="404">No project has that id.</response>
    /// <response code="502">The Project Service could not be reached to check access.</response>
    [HttpGet("projects/{projectId:guid}/documents")]
    [Authorize(Roles = PlatformRoles.AnyRole)]
    [ProducesResponseType(typeof(IReadOnlyList<DesignDocumentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ListForProject(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out _) || CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        if (await CheckProjectAccessAsync(projectId, token, cancellationToken) is { } denied)
        {
            return denied;
        }

        var documents = await _repository.ListForProjectAsync(projectId);

        return Ok(documents.Select(DesignDocumentResponse.From).ToList());
    }

    /// <summary>
    /// Downloads the file stored for one version. Allowed roles: Client,
    /// Architect, Project Manager, Admin.
    /// </summary>
    /// <response code="200">The file, with its original name and type.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not party to the project this version belongs to.</response>
    /// <response code="404">No version has that id.</response>
    /// <response code="502">The Project Service could not be reached to check access.</response>
    [HttpGet("versions/{versionId:guid}/file")]
    [Authorize(Roles = PlatformRoles.AnyRole)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> DownloadVersionFile(Guid versionId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out _) || CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        var file = await _repository.GetVersionFileAsync(versionId);

        if (file is null)
        {
            return VersionNotFoundProblem(versionId);
        }

        // The access check is on the project the version belongs to, read from
        // storage — not on anything the caller supplied.
        if (await CheckProjectAccessAsync(file.ProjectId, token, cancellationToken) is { } denied)
        {
            return denied;
        }

        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Approves a design document version. Allowed roles: Client.
    /// </summary>
    /// <remarks>
    /// Records the approving user and timestamp, and enqueues a
    /// <c>DesignApproved</c> event in the same transaction as the decision —
    /// see <see cref="IDesignDocumentRepository.RecordReviewDecisionAsync"/>.
    /// Refused with 409 if the version has already been decided, or if another
    /// version of the same document is already approved: once one is, every
    /// other version of that document is read-only history.
    /// </remarks>
    /// <response code="200">Recorded — the decision, who made it, and when.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client, or is not party to this project.</response>
    /// <response code="404">No version has that id.</response>
    /// <response code="409">The version was already decided, or the document already has an approved version.</response>
    /// <response code="502">The Project Service could not be reached to check access.</response>
    [HttpPost("versions/{versionId:guid}/approve")]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ReviewDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<IActionResult> Approve(Guid versionId, CancellationToken cancellationToken) =>
        ReviewAsync(versionId, DesignDocumentStatus.Approved, reviewComment: null, cancellationToken);

    /// <summary>
    /// Asks for changes on a design document version, with a comment on what
    /// needs to change. Allowed roles: Client.
    /// </summary>
    /// <remarks>
    /// No event is raised for this outcome — US-11 names <c>DesignApproved</c>
    /// only. Notifying the Architect is a separate step from recording the
    /// decision here.
    /// </remarks>
    /// <response code="200">Recorded — the decision, who made it, when, and the comment.</response>
    /// <response code="400">The comment was missing or too long.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client, or is not party to this project.</response>
    /// <response code="404">No version has that id.</response>
    /// <response code="409">The version was already decided, or the document already has an approved version.</response>
    /// <response code="502">The Project Service could not be reached to check access.</response>
    [HttpPost("versions/{versionId:guid}/request-revision")]
    [Authorize(Roles = PlatformRoles.Client)]
    [ProducesResponseType(typeof(ReviewDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<IActionResult> RequestRevision(
        Guid versionId,
        [FromBody] RequestRevisionRequest request,
        CancellationToken cancellationToken) =>
        ReviewAsync(versionId, DesignDocumentStatus.RevisionRequested, request.Comment.Trim(), cancellationToken);

    /// <summary>
    /// The common path behind <see cref="Approve"/> and
    /// <see cref="RequestRevision"/>: read the version, check access, record
    /// the decision, and translate the outcome into a response.
    /// </summary>
    private async Task<IActionResult> ReviewAsync(
        Guid versionId,
        DesignDocumentStatus status,
        string? reviewComment,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var clientId) || CallerBearerToken() is not { } token)
        {
            return Unauthorized();
        }

        var version = await _repository.GetVersionForReviewAsync(versionId);

        if (version is null)
        {
            return VersionNotFoundProblem(versionId);
        }

        // The access check is on the project the version belongs to, read from
        // storage — not on anything the caller supplied.
        if (await CheckProjectAccessAsync(version.ProjectId, token, cancellationToken) is { } denied)
        {
            return denied;
        }

        var reviewedAt = DateTime.UtcNow;

        var decision = new ReviewDecision
        {
            VersionId = versionId,
            DocumentId = version.DocumentId,
            Status = status,
            ReviewedBy = clientId,
            ReviewedAtUtc = reviewedAt,
            ReviewComment = reviewComment
        };

        // US-23: each review outcome raises its own event. The comment is only
        // present for a revision request, hence the non-null assert there.
        IReadOnlyList<OutboxEvent> outboxEvents = status == DesignDocumentStatus.Approved
            ? [DesignEvents.Approved(version, clientId, reviewedAt)]
            : [DesignEvents.RevisionRequested(version, clientId, reviewComment!, reviewedAt)];

        var outcome = await _repository.RecordReviewDecisionAsync(decision, outboxEvents);

        var displayName = $"{version.DocumentName}_v{version.VersionNumber}";

        switch (outcome)
        {
            case ReviewDecisionOutcome.Recorded:
                _logger.LogInformation(
                    "Client {ClientId} set {DisplayName} to {Status}.", clientId, displayName, status);

                if (status == DesignDocumentStatus.RevisionRequested)
                {
                    await NotifyArchitectAsync(version, displayName, reviewComment!, cancellationToken);
                }

                return Ok(new ReviewDecisionResponse
                {
                    VersionId = versionId,
                    DocumentId = version.DocumentId,
                    DisplayName = displayName,
                    Status = status.ToString(),
                    ReviewedBy = clientId,
                    ReviewedAt = reviewedAt,
                    ReviewComment = reviewComment
                });

            case ReviewDecisionOutcome.VersionNotFound:
                // The version existed a moment ago, above, and was removed
                // between then and the write — not something a caller can
                // usefully retry differently, but a 404 is still the honest
                // answer.
                return VersionNotFoundProblem(versionId);

            case ReviewDecisionOutcome.AlreadyDecided:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Already reviewed",
                    detail: $"{displayName} has already had a decision recorded on it.");

            case ReviewDecisionOutcome.DocumentAlreadyApproved:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Design already approved",
                    detail: "Another version of this document has already been approved. Every version of "
                            + "an approved document is read-only.");

            default:
                throw new InvalidOperationException($"Unhandled {nameof(ReviewDecisionOutcome)}: {outcome}.");
        }
    }

    /// <summary>
    /// Tells the Architect who uploaded the version that a revision was
    /// requested, and what for.
    /// </summary>
    /// <remarks>
    /// The review decision is already recorded by the time this runs — a
    /// notification that could not be sent (the User Service unreachable, the
    /// mail server down) is logged and swallowed here rather than turned into a
    /// failed request, the same reasoning <c>AuthController</c>'s password-reset
    /// email follows.
    /// </remarks>
    private async Task NotifyArchitectAsync(
        DesignVersionForReview version, string displayName, string comment, CancellationToken cancellationToken)
    {
        try
        {
            await _revisionRequestNotifier.NotifyAsync(version.UploadedBy, displayName, comment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not notify Architect {ArchitectId} about the revision requested on {DisplayName}.",
                version.UploadedBy,
                displayName);
        }
    }

    /// <summary>
    /// Asks the Project Service whether the caller may work with this project.
    /// Returns <c>null</c> to carry on, or the response to send back instead.
    /// </summary>
    private async Task<IActionResult?> CheckProjectAccessAsync(
        Guid projectId,
        string token,
        CancellationToken cancellationToken)
    {
        var access = await _projectAccess.GetAccessAsync(projectId, token, cancellationToken);

        return access.Outcome switch
        {
            ProjectAccessOutcome.Allowed => null,

            ProjectAccessOutcome.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Project not found",
                detail: $"No project with id '{projectId}' exists."),

            ProjectAccessOutcome.Forbidden => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your project",
                detail: "Only the client who submitted this project, the staff assigned to it, or an "
                        + "administrator can work with its design documents."),

            _ => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Project could not be verified",
                detail: "The project this design document belongs to could not be verified right now. "
                        + "Try again in a moment."),
        };
    }

    private IActionResult VersionNotFoundProblem(Guid versionId) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Version not found",
            detail: $"No design document version with id '{versionId}' exists.");

    private IActionResult FileProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "The file was not accepted", detail: detail);

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    /// <summary>
    /// The caller's id from the token's <c>sub</c> claim. A token that passed
    /// validation but carries no usable subject is not something to act on.
    /// </summary>
    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    /// <summary>
    /// The raw access token from the <c>Authorization</c> header, without the
    /// <c>Bearer </c> prefix — forwarded to the Project Service so it decides as
    /// if the caller asked it directly.
    /// </summary>
    private string? CallerBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim() is { Length: > 0 } value ? value : null
            : null;
    }

    /// <summary>Blank in, <c>null</c> out — an empty revision comment is stored as nothing.</summary>
    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
