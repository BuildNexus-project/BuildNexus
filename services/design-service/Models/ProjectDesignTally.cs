namespace BuildNexus.DesignService.Models;

/// <summary>
/// How one project's design documents divide by the review state of their latest
/// version — the figures the Client's dashboard reads a project's design status
/// from (US-21 AC-1).
/// </summary>
/// <remarks>
/// Only <em>latest</em> versions are counted, so the three counts always add up
/// to <see cref="DocumentCount"/>: a document is in exactly one state. A project
/// with no documents has no tally at all — <see cref="Empty"/> is what the
/// caller builds for it.
/// </remarks>
public class ProjectDesignTally
{
    public required Guid ProjectId { get; init; }

    /// <summary>How many design documents the project has.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>
    /// Documents whose latest version is <see cref="DesignDocumentStatus.Submitted"/> or
    /// <see cref="DesignDocumentStatus.UnderReview"/> — waiting on the Client's decision.
    /// </summary>
    public required int AwaitingReviewCount { get; init; }

    /// <summary>
    /// Documents whose latest version is <see cref="DesignDocumentStatus.RevisionRequested"/> —
    /// waiting on the Architect.
    /// </summary>
    public required int RevisionRequestedCount { get; init; }

    /// <summary>Documents whose latest version is <see cref="DesignDocumentStatus.Approved"/>.</summary>
    public required int ApprovedCount { get; init; }

    /// <summary>The project's design as one word — see <see cref="ProjectDesignState"/>.</summary>
    public ProjectDesignState State =>
        DocumentCount == 0 ? ProjectDesignState.NoDesign
        : AwaitingReviewCount > 0 ? ProjectDesignState.AwaitingReview
        : RevisionRequestedCount > 0 ? ProjectDesignState.RevisionRequested
        : ProjectDesignState.Approved;

    /// <summary>The tally of a project nothing has been uploaded for.</summary>
    public static ProjectDesignTally Empty(Guid projectId) => new()
    {
        ProjectId = projectId,
        DocumentCount = 0,
        AwaitingReviewCount = 0,
        RevisionRequestedCount = 0,
        ApprovedCount = 0
    };
}
