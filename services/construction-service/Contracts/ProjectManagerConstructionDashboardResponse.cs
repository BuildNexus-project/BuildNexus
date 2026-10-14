using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// A Project Manager's dashboard (US-21 AC-3): the builds under way on the projects they are
/// assigned to, and the milestones still to finish on them.
/// </summary>
/// <remarks>
/// Scoped to the Project Manager's own projects. This service does not know which Project
/// Manager runs a project, so it asks the Project Service — with the caller's own token — for
/// the projects they are assigned to and reads only those. That answer also names them, so
/// every build and milestone here carries its project's name and the page needs no second
/// request to say which one it means.
/// </remarks>
public class ProjectManagerConstructionDashboardResponse
{
    /// <summary>How many builds are under way — the length of <see cref="ActiveBuilds"/>.</summary>
    public int ActiveBuildCount { get; set; }

    /// <summary>
    /// The builds under way — started and not yet handed over — most recently started first.
    /// Empty is a real answer when nothing has been started, or when the Project Manager has
    /// no project assigned.
    /// </summary>
    public IReadOnlyList<ActiveBuildResponse> ActiveBuilds { get; set; } = [];

    /// <summary>The milestones still to finish on those builds.</summary>
    public MilestonesDueResponse MilestonesDue { get; set; } = new();

    public class ActiveBuildResponse
    {
        public Guid ProjectId { get; set; }

        /// <summary>The project's name, as the Project Service gave it.</summary>
        public string? ProjectName { get; set; }

        /// <summary>Serialised as its name — <c>"Started"</c> or <c>"Completed"</c>.</summary>
        public ConstructionPhaseStatus? PhaseStatus { get; set; }

        public int TotalMilestones { get; set; }

        public int CompletedMilestones { get; set; }

        /// <summary>What is left on this build: the milestones that are not <c>Completed</c>.</summary>
        public int OutstandingMilestones { get; set; }

        public decimal ProgressPercent { get; set; }

        public static ActiveBuildResponse From(ConstructionProgressReportRow row, string? projectName) => new()
        {
            ProjectId = row.ProjectId,
            ProjectName = projectName,
            PhaseStatus = row.PhaseStatus,
            TotalMilestones = row.TotalMilestones,
            CompletedMilestones = row.CompletedMilestones,
            OutstandingMilestones = row.TotalMilestones - row.CompletedMilestones,
            ProgressPercent = row.ProgressPercent
        };
    }

    /// <summary>
    /// "Due" means outstanding — every milestone not yet completed — and, where the Project
    /// Manager has given a milestone a due date, some of those are also overdue.
    /// </summary>
    public class MilestonesDueResponse
    {
        /// <summary>Every outstanding milestone on a running build — not limited by the length of <see cref="Milestones"/>.</summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// How many of <see cref="TotalCount"/> are overdue: they have a due date and it has
        /// passed. A milestone with no date is outstanding and never overdue.
        /// </summary>
        public int OverdueCount { get; set; }

        /// <summary>
        /// The first few: those with a due date first, soonest (most overdue) first, then those
        /// without one; already-in-progress ones ahead of not-started ones within a date.
        /// </summary>
        public IReadOnlyList<MilestoneDueResponse> Milestones { get; set; } = [];
    }

    public class MilestoneDueResponse
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        /// <summary>The project's name, as the Project Service gave it.</summary>
        public string? ProjectName { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Serialised as its name — <c>"NotStarted"</c> or <c>"InProgress"</c>.</summary>
        public MilestoneStatus Status { get; set; }

        /// <summary>The day it should be finished by, as <c>yyyy-MM-dd</c>, or <c>null</c> when none was set.</summary>
        public DateOnly? DueDate { get; set; }

        /// <summary>Whether <see cref="DueDate"/> is before today. <c>false</c> for a milestone with no date.</summary>
        public bool IsOverdue { get; set; }

        public DateTime CreatedAt { get; set; }

        public static MilestoneDueResponse From(OutstandingMilestone milestone, string? projectName, DateOnly today) => new()
        {
            Id = milestone.Id,
            ProjectId = milestone.ProjectId,
            ProjectName = projectName,
            Name = milestone.Name,
            Status = milestone.Status,
            DueDate = milestone.DueDate,
            IsOverdue = milestone.DueDate is { } due && due < today,
            CreatedAt = milestone.CreatedAtUtc
        };
    }
}
