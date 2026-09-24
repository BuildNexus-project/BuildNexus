using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// A project's construction phase as the API reports it (US-14) — the body of a
/// successful start or complete, and of the phase read the PM's screen uses to
/// decide which transition is available next.
/// </summary>
/// <remarks>
/// A near-mirror of <see cref="ConstructionPhase"/>, kept separate for the same
/// reason <see cref="MilestoneResponse"/> is: the wire contract should not move if
/// the model later gains a field this response should not expose.
/// <para>
/// The two nullable timestamps are the shape the frontend reads to decide what the
/// build has been through — a phase with a <see cref="CompletedAtUtc"/> and no
/// <see cref="HandedOverAtUtc"/> is a finished build awaiting handover — so they
/// are sent as explicit nulls rather than omitted.
/// </para>
/// </remarks>
public class ConstructionPhaseResponse
{
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Serialised as its name (<c>"Started"</c>, <c>"Completed"</c>,
    /// <c>"HandedOver"</c>) via the <c>JsonStringEnumConverter</c> registered in
    /// <c>Program.cs</c> — kinder to the React side than an integer, and safe
    /// against a member being reordered.
    /// </summary>
    public ConstructionPhaseStatus Status { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary><c>null</c> until construction is marked complete.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary><c>null</c> until the project is handed over to the Client.</summary>
    public DateTime? HandedOverAtUtc { get; set; }

    /// <summary>
    /// The Project Manager who handed the project over; <c>null</c> until then.
    /// Handover raises no event, so this is the only record of who ended the project.
    /// </summary>
    public Guid? HandedOverByUserId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public static ConstructionPhaseResponse From(ConstructionPhase phase) => new()
    {
        ProjectId = phase.ProjectId,
        Status = phase.Status,
        StartedAtUtc = phase.StartedAtUtc,
        CompletedAtUtc = phase.CompletedAtUtc,
        HandedOverAtUtc = phase.HandedOverAtUtc,
        HandedOverByUserId = phase.HandedOverByUserId,
        UpdatedAtUtc = phase.UpdatedAtUtc
    };
}
