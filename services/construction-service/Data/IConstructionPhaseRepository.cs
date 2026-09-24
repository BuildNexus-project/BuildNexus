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
    AlreadyCompleted
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
    /// The project's construction phase, or <c>null</c> when the build has not
    /// been started.
    /// </summary>
    Task<ConstructionPhase?> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
