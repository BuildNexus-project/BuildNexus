using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// A Project Manager's dashboard (US-21 AC-3): the builds that are under way, and
/// the milestones still to finish on them.
/// </summary>
/// <remarks>
/// Portfolio-wide, like the construction report: this service records who owns a
/// project but not which Project Manager runs it, so it cannot narrow to "your"
/// builds. Names for the projects come from the Project Service's own data; the
/// page joins them on the project id.
/// </remarks>
public class ProjectManagerConstructionDashboardResponse
{
    /// <summary>How many builds are under way — the length of <see cref="ActiveBuilds"/>.</summary>
    public int ActiveBuildCount { get; set; }

    /// <summary>
    /// The builds under way — started and not yet handed over — most recently
    /// started first. Empty is a real answer when nothing has been started.
    /// </summary>
    public IReadOnlyList<ActiveBuildResponse> ActiveBuilds { get; set; } = [];

    /// <summary>The milestones still to finish on those builds.</summary>
    public MilestonesDueResponse MilestonesDue { get; set; } = new();

    public class ActiveBuildResponse
    {
        public Guid ProjectId { get; set; }

        /// <summary>Serialised as its name — <c>"Started"</c> or <c>"Completed"</c>.</summary>
        public ConstructionPhaseStatus? PhaseStatus { get; set; }

        public int TotalMilestones { get; set; }

        public int CompletedMilestones { get; set; }

        /// <summary>What is left on this build: the milestones that are not <c>Completed</c>.</summary>
        public int OutstandingMilestones { get; set; }

        public decimal ProgressPercent { get; set; }

        public static ActiveBuildResponse From(ConstructionProgressReportRow row) => new()
        {
            ProjectId = row.ProjectId,
            PhaseStatus = row.PhaseStatus,
            TotalMilestones = row.TotalMilestones,
            CompletedMilestones = row.CompletedMilestones,
            OutstandingMilestones = row.TotalMilestones - row.CompletedMilestones,
            ProgressPercent = row.ProgressPercent
        };
    }

    /// <summary>
    /// "Due" means outstanding: milestones have no due date, so this is what is
    /// still to finish, not what is late.
    /// </summary>
    public class MilestonesDueResponse
    {
        /// <summary>Every outstanding milestone on a running build — not limited by the length of <see cref="Milestones"/>.</summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// The first few: already-in-progress ones first, then those not started,
        /// each in the order they were planned.
        /// </summary>
        public IReadOnlyList<MilestoneDueResponse> Milestones { get; set; } = [];
    }

    public class MilestoneDueResponse
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Serialised as its name — <c>"NotStarted"</c> or <c>"InProgress"</c>.</summary>
        public MilestoneStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public static MilestoneDueResponse From(OutstandingMilestone milestone) => new()
        {
            Id = milestone.Id,
            ProjectId = milestone.ProjectId,
            Name = milestone.Name,
            Status = milestone.Status,
            CreatedAt = milestone.CreatedAtUtc
        };
    }
}
