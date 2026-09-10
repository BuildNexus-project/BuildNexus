using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// The <c>payload</c> of a <see cref="DesignEventTypes.DesignRevisionRequested"/>
/// event: which version a Client asked for changes on, and what they asked for.
/// </summary>
/// <remarks>
/// No consumer subscribes to this yet — US-23 names it alongside the other two.
/// The comment travels on the event so a consumer would not have to fetch it.
/// </remarks>
public sealed class DesignRevisionRequestedPayload
{
    public required Guid DocumentId { get; init; }

    public required Guid ProjectId { get; init; }

    public required string DocumentName { get; init; }

    public required Guid VersionId { get; init; }

    public required int VersionNumber { get; init; }

    /// <summary>The Client who asked for the revision.</summary>
    public required Guid RequestedByUserId { get; init; }

    /// <summary>The Client's note on what needs to change.</summary>
    public required string Comment { get; init; }

    /// <summary>Offset-bearing, so the instant is unambiguous on the wire.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    public static DesignRevisionRequestedPayload From(
        DesignVersionForReview version, Guid requestedBy, string comment, DateTime requestedAtUtc) =>
        new()
        {
            DocumentId = version.DocumentId,
            ProjectId = version.ProjectId,
            DocumentName = version.DocumentName,
            VersionId = version.VersionId,
            VersionNumber = version.VersionNumber,
            RequestedByUserId = requestedBy,
            Comment = comment,
            RequestedAt = EventTimestamp.AsUtc(requestedAtUtc)
        };
}
