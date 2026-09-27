using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// One project's line in the construction progress report (US-19 AC-1), as the API
/// reports it.
/// </summary>
/// <remarks>
/// A near-mirror of <see cref="ConstructionProgressReportRow"/>, kept separate for the
/// same reason every other response in this service is: the wire contract should not move
/// if the model later gains a field this report should not expose.
/// <para>
/// The breakdown counts are sent alongside the percentage rather than instead of it. The
/// percentage answers "how far along is this build", the counts answer "and what is
/// actually outstanding" — a report that gave only the first would have the reader
/// guessing at the second, and one that gave only the counts would have them doing the
/// division.
/// </para>
/// </remarks>
public class ConstructionProgressReportRowResponse
{
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Serialised as its name (<c>"Started"</c>, <c>"Completed"</c>,
    /// <c>"HandedOver"</c>) by the converter registered in <c>Program.cs</c>, or
    /// <c>null</c> when construction has not been started on the project.
    /// </summary>
    public ConstructionPhaseStatus? PhaseStatus { get; set; }

    public int TotalMilestones { get; set; }

    public int CompletedMilestones { get; set; }

    public int InProgressMilestones { get; set; }

    public int NotStartedMilestones { get; set; }

    public decimal ProgressPercent { get; set; }

    public static ConstructionProgressReportRowResponse From(ConstructionProgressReportRow row) => new()
    {
        ProjectId = row.ProjectId,
        PhaseStatus = row.PhaseStatus,
        TotalMilestones = row.TotalMilestones,
        CompletedMilestones = row.CompletedMilestones,
        InProgressMilestones = row.InProgressMilestones,
        NotStartedMilestones = row.NotStartedMilestones,
        ProgressPercent = row.ProgressPercent
    };
}
