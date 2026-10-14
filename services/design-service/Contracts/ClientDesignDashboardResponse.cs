using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// The design half of a Client's dashboard (US-21 AC-1): where the design stands
/// on each of their active projects.
/// </summary>
/// <remarks>
/// Only the half this service owns. The projects themselves, build progress and
/// payments due arrive from the Project, Construction and Payment services' own
/// dashboard endpoints; the page joins the four on the project id.
/// </remarks>
public class ClientDesignDashboardResponse
{
    /// <summary>
    /// One entry per active project the Client has, in the order the Project
    /// Service lists them — including a project nothing has been uploaded for,
    /// which is reported as <c>NoDesign</c> rather than left out.
    /// </summary>
    public IReadOnlyList<ProjectDesignStatusResponse> Projects { get; set; } = [];

    public class ProjectDesignStatusResponse
    {
        public Guid ProjectId { get; set; }

        /// <summary>
        /// <c>NoDesign</c>, <c>AwaitingReview</c>, <c>RevisionRequested</c> or
        /// <c>Approved</c> — see <see cref="ProjectDesignState"/> for what each
        /// means and which outranks which.
        /// </summary>
        public string State { get; set; } = string.Empty;

        public int DocumentCount { get; set; }

        /// <summary>Documents whose latest version is waiting on the Client's decision.</summary>
        public int AwaitingReviewCount { get; set; }

        /// <summary>Documents whose latest version the Client sent back and the Architect has not answered.</summary>
        public int RevisionRequestedCount { get; set; }

        public int ApprovedCount { get; set; }

        public static ProjectDesignStatusResponse From(ProjectDesignTally tally) => new()
        {
            ProjectId = tally.ProjectId,
            State = tally.State.ToString(),
            DocumentCount = tally.DocumentCount,
            AwaitingReviewCount = tally.AwaitingReviewCount,
            RevisionRequestedCount = tally.RevisionRequestedCount,
            ApprovedCount = tally.ApprovedCount
        };
    }
}
