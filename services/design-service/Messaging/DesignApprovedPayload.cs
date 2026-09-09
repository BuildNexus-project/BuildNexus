using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// The <c>payload</c> of a <see cref="DesignEventTypes.DesignApproved"/> event:
/// which version was signed off, of which document and project, and who signed
/// it off.
/// </summary>
/// <remarks>
/// Carries what a consumer needs to act — for example the Project Service
/// deciding whether a project's design stage is complete — without it having to
/// call back here for the project or document a version belongs to.
/// </remarks>
public sealed class DesignApprovedPayload
{
    public required Guid DocumentId { get; init; }

    /// <summary>So a consumer that only tracks projects, not documents, has what it needs.</summary>
    public required Guid ProjectId { get; init; }

    public required string DocumentName { get; init; }

    public required Guid VersionId { get; init; }

    public required int VersionNumber { get; init; }

    /// <summary>The Client who approved it.</summary>
    public required Guid ApprovedByUserId { get; init; }

    /// <summary>
    /// Offset-bearing, so the instant is unambiguous on the wire.
    /// </summary>
    public required DateTimeOffset ApprovedAt { get; init; }

    public static DesignApprovedPayload From(DesignVersionForReview version, Guid approvedBy, DateTime approvedAtUtc) =>
        new()
        {
            DocumentId = version.DocumentId,
            ProjectId = version.ProjectId,
            DocumentName = version.DocumentName,
            VersionId = version.VersionId,
            VersionNumber = version.VersionNumber,
            ApprovedByUserId = approvedBy,
            ApprovedAt = EventTimestamp.AsUtc(approvedAtUtc)
        };
}
