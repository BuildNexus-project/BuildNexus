namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// Where a project's build phase stands, once it has formally begun (US-14).
/// Persisted as its own name (<c>"Started"</c>, <c>"Completed"</c>,
/// <c>"HandedOver"</c>) rather than an integer, the same way
/// <see cref="MilestoneStatus"/> is — so a DBA reading the table can see what a
/// row means, and inserting a member here cannot silently re-label existing rows.
/// </summary>
/// <remarks>
/// There is deliberately no <c>NotStarted</c> member. "Construction has not
/// started" is the absence of a <c>construction_phases</c> row, not a value in
/// it: a row exists precisely because a Project Manager pressed start, and
/// giving that state a name here would invite a row that claims construction
/// started while saying it has not.
/// </remarks>
public enum ConstructionPhaseStatus
{
    /// <summary>The build is under way. The state every phase begins in.</summary>
    Started,

    /// <summary>
    /// Every milestone is done and the Project Manager has marked the build
    /// complete. Not terminal — handover still follows.
    /// </summary>
    Completed,

    /// <summary>
    /// The finished project has been handed to the Client. Terminal: nothing
    /// follows it, and no transition may leave it (AC-4).
    /// </summary>
    HandedOver
}

/// <summary>
/// One project's construction phase — the record that the build was started,
/// and how far through its end-to-end lifecycle it has travelled (US-14).
/// </summary>
/// <remarks>
/// Owned entirely by the Construction Service. The project's own
/// <c>status</c> column lives in the Project Service and is moved by that
/// service consuming the events this one publishes — this row is not a copy of
/// it, it is the construction phase's own state.
/// </remarks>
public class ConstructionPhase
{
    /// <summary>
    /// The project being built. Not a foreign key across a service boundary —
    /// copied off the <c>milestone_setups</c> row the <c>DesignApproved</c>
    /// consumer planted.
    /// </summary>
    public required Guid ProjectId { get; init; }

    public required ConstructionPhaseStatus Status { get; init; }

    /// <summary>When the Project Manager started the build. Always set.</summary>
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>When construction was marked complete; <c>null</c> until then.</summary>
    public DateTime? CompletedAtUtc { get; init; }

    /// <summary>When the project was handed over to the Client; <c>null</c> until then.</summary>
    public DateTime? HandedOverAtUtc { get; init; }

    /// <summary>Stamped on every transition, so a caller can see when the phase last moved.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}
