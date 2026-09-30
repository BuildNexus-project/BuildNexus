using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// The progress half of a Client's dashboard (US-21 AC-1): how far along the
/// build of each of their projects is.
/// </summary>
/// <remarks>
/// Only the half this service owns. The projects' names and status, design status
/// and payments due arrive from the Project, Design and Payment services' own
/// dashboard endpoints; the page joins the four on the project id.
/// </remarks>
public class ClientConstructionDashboardResponse
{
    /// <summary>
    /// One entry per project the Client owns that has milestones planned and has
    /// not been handed over, ordered by project id. A project with nothing planned
    /// is not here — it has no progress to report, and a zero would read as a
    /// stalled build.
    /// </summary>
    public IReadOnlyList<ProjectBuildProgressResponse> Projects { get; set; } = [];

    public class ProjectBuildProgressResponse
    {
        public Guid ProjectId { get; set; }

        /// <summary>
        /// Serialised as its name (<c>"Started"</c>, <c>"Completed"</c>) by the
        /// converter registered in <c>Program.cs</c>, or <c>null</c> when the build
        /// is planned but has not been started.
        /// </summary>
        public ConstructionPhaseStatus? PhaseStatus { get; set; }

        public int TotalMilestones { get; set; }

        public int CompletedMilestones { get; set; }

        /// <summary>Completed over total, as a percentage to two decimal places.</summary>
        public decimal ProgressPercent { get; set; }

        public static ProjectBuildProgressResponse From(ConstructionProgressReportRow row) => new()
        {
            ProjectId = row.ProjectId,
            PhaseStatus = row.PhaseStatus,
            TotalMilestones = row.TotalMilestones,
            CompletedMilestones = row.CompletedMilestones,
            ProgressPercent = row.ProgressPercent
        };
    }
}
