using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Why a construction transition was allowed, or the specific precondition that
/// refused it (AC-3).
/// </summary>
/// <remarks>
/// One member per reason rather than a bare <c>bool</c> or a <c>null</c>, because
/// AC-3 is not "reject invalid transitions" but "reject them for a reason the
/// Project Manager can act on": a start refused because no milestones exist needs
/// a different sentence on screen than one refused because the design is not
/// approved, and the controller cannot invent that distinction from a <c>false</c>.
/// <para>
/// The members are grouped by the transition that can produce them. Each
/// transition checks its own preconditions independently — a gate is never
/// inferred from another gate having passed.
/// </para>
/// </remarks>
public enum ConstructionTransitionOutcome
{
    /// <summary>The transition was applied and the phase row reflects it.</summary>
    Succeeded,

    // ---- start ----

    /// <summary>
    /// No <c>milestone_setups</c> row — the project's <c>DesignApproved</c> event
    /// has not been consumed, so its design is not approved.
    /// </summary>
    DesignNotApproved,

    /// <summary>
    /// The design is approved but the project has no milestones. AC-1 requires
    /// both, so this is its own answer rather than being folded into the one above.
    /// </summary>
    NoMilestonesDefined,

    /// <summary>A <c>construction_phases</c> row already exists — the build is already under way, or past it.</summary>
    AlreadyStarted,

    // ---- complete ----

    /// <summary>
    /// No <c>construction_phases</c> row — construction was never started, so
    /// there is nothing to complete. AC-3 names this case explicitly.
    /// </summary>
    NotStarted,

    /// <summary>
    /// At least one milestone is not <c>Completed</c>, or the project has none at
    /// all. AC-2 requires every milestone done; zero milestones does not satisfy
    /// "every one is complete" in any useful sense, and a build with no plan
    /// cannot be finished.
    /// </summary>
    MilestonesIncomplete,

    /// <summary>Construction is already marked complete, or already handed over.</summary>
    AlreadyCompleted,

    // ---- handover ----

    /// <summary>
    /// The build is still under way. AC-4 hands over a <em>finished</em> project, so
    /// a <c>Started</c> phase is refused — distinct from
    /// <see cref="NotStarted"/>, which is the project that never began.
    /// </summary>
    NotCompleted,

    /// <summary>
    /// The project's final payment is not recorded as settled — AC-4's second
    /// precondition, read from the local marker the <c>FinalPaymentSettled</c>
    /// consumer plants.
    /// </summary>
    /// <remarks>
    /// Also the answer while the Payment Service is unbuilt and publishes nothing, so
    /// every handover is refused this way until it ships. That is the safe direction:
    /// a project held back can be handed over once the marker arrives, whereas one
    /// handed over unpaid cannot be un-handed.
    /// </remarks>
    FinalPaymentNotSettled,

    /// <summary>The project has already been handed over. Terminal — nothing follows it.</summary>
    AlreadyHandedOver
}

/// <summary>
/// The outcome of a transition attempt, with the phase as it now stands when the
/// attempt succeeded.
/// </summary>
/// <remarks>
/// <see cref="Phase"/> is non-null exactly when <see cref="Outcome"/> is
/// <see cref="ConstructionTransitionOutcome.Succeeded"/> — a refused transition
/// wrote nothing, so there is no new state to report.
/// </remarks>
public sealed record ConstructionTransitionResult(
    ConstructionTransitionOutcome Outcome,
    ConstructionPhase? Phase)
{
    public static ConstructionTransitionResult Rejected(ConstructionTransitionOutcome outcome) =>
        new(outcome, Phase: null);

    public static ConstructionTransitionResult Succeeded(ConstructionPhase phase) =>
        new(ConstructionTransitionOutcome.Succeeded, phase);
}

/// <summary>Data access for <c>construction_phases</c>.</summary>
public interface IConstructionPhaseRepository
{
    /// <summary>
    /// Records that a Project Manager has started the build (AC-1).
    /// </summary>
    /// <remarks>
    /// Two preconditions, checked independently and in the same transaction as
    /// the insert, so neither can be satisfied by state that changes underneath:
    /// the project must have a <c>milestone_setups</c> row (its design is
    /// approved — US-12's gate, read here rather than re-implemented), and it
    /// must have at least one milestone defined. A project whose build has
    /// already started is refused by the <c>project_id</c> primary key rather
    /// than by a check that read before the other writer committed.
    /// </remarks>
    /// <param name="startedBy">
    /// The Project Manager making the decision, from their token's <c>sub</c>
    /// claim. Not used by any gate — it rides onto the <c>ConstructionStarted</c>
    /// event so the Project Service can attribute the status change it makes in
    /// reaction.
    /// </param>
    Task<ConstructionTransitionResult> StartAsync(
        Guid projectId,
        Guid startedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the build complete (AC-2).
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="StartAsync"/>'s gates: this one requires a
    /// <c>Started</c> phase row and every one of the project's milestones to be
    /// <c>Completed</c>. The milestone tally is read straight from
    /// <c>construction_milestones</c> — US-12 owns those statuses and this story
    /// only reads them.
    /// </remarks>
    /// <param name="completedBy">
    /// The Project Manager making the decision, carried onto the
    /// <c>ConstructionCompleted</c> event for the same reason
    /// <see cref="StartAsync"/>'s actor is.
    /// </param>
    Task<ConstructionTransitionResult> CompleteAsync(
        Guid projectId,
        Guid completedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands the finished project over to the Client, moving it to its terminal state
    /// (AC-4).
    /// </summary>
    /// <remarks>
    /// Two preconditions, checked independently of each other and of the earlier
    /// transitions' gates: the phase must be <c>Completed</c>, and the project's final
    /// payment must be recorded as settled. The settlement is read from
    /// <c>payment_settlements</c> in the same transaction as the write — the local
    /// replica of a Payment Service fact, so no cross-service call sits on this path.
    /// <para>
    /// Publishes nothing. US-14 names two events and this is not one of them: the
    /// Project Service reaches <c>Completed</c> on <c>ConstructionCompleted</c>, and
    /// the handover itself is this service's own terminal state plus the summary the
    /// Client reads from it.
    /// </para>
    /// </remarks>
    /// <param name="handedOverBy">
    /// The Project Manager actioning the handover. Recorded on the phase row as the
    /// author of the terminal move.
    /// </param>
    Task<ConstructionTransitionResult> HandOverAsync(
        Guid projectId,
        Guid handedOverBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The project's construction phase, or <c>null</c> when the build has not
    /// been started.
    /// </summary>
    Task<ConstructionPhase?> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
