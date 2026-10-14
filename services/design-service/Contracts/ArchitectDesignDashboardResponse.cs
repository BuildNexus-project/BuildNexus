using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// The design half of an Architect's dashboard (US-21 AC-2): the revisions
/// Clients have asked for that the Architect has not yet answered.
/// </summary>
/// <remarks>
/// The assigned projects themselves are the Project Service's data and come from
/// its own dashboard endpoint; the page joins the two on the project id.
/// </remarks>
public class ArchitectDesignDashboardResponse
{
    /// <summary>How many revisions are outstanding — the length of <see cref="Revisions"/>.</summary>
    public int PendingRevisionCount { get; set; }

    /// <summary>Longest-waiting first. Empty is a real answer for an Architect with nothing to redo.</summary>
    public IReadOnlyList<PendingRevisionResponse> Revisions { get; set; } = [];

    public class PendingRevisionResponse
    {
        public Guid ProjectId { get; set; }

        public Guid DocumentId { get; set; }

        /// <summary>The document's name — "GroundFloorPlan", "Elevations".</summary>
        public string DocumentName { get; set; } = string.Empty;

        /// <summary>The version the Client sent back for changes.</summary>
        public int VersionNumber { get; set; }

        /// <summary>What the Client asked to change.</summary>
        public string? ReviewComment { get; set; }

        /// <summary>When the Client asked.</summary>
        public DateTime RequestedAt { get; set; }

        public static PendingRevisionResponse From(PendingRevision revision) => new()
        {
            ProjectId = revision.ProjectId,
            DocumentId = revision.DocumentId,
            DocumentName = revision.DocumentName,
            VersionNumber = revision.VersionNumber,
            ReviewComment = revision.ReviewComment,
            RequestedAt = revision.RequestedAtUtc
        };
    }
}
