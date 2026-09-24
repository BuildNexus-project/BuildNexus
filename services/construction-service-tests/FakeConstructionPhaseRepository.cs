using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IConstructionPhaseRepository"/> that records the calls made to it
/// and answers with whatever outcome a test asks for, so the
/// <see cref="Controllers.ConstructionPhaseController"/> suite can check the
/// HTTP mapping without MySQL.
/// </summary>
/// <remarks>
/// The preconditions themselves are not simulated here — they are SQL, and
/// <see cref="ConstructionPhaseRepositoryDatabaseTests"/> covers them against the
/// real engine. What this stands in for is the outcome the controller has to
/// translate, so every branch of AC-3's refusals can be driven directly.
/// </remarks>
public sealed class FakeConstructionPhaseRepository : IConstructionPhaseRepository
{
    /// <summary>One entry per <see cref="StartAsync"/> call, in order.</summary>
    public List<TransitionCall> StartCalls { get; } = [];

    /// <summary>One entry per <see cref="CompleteAsync"/> call, in order.</summary>
    public List<TransitionCall> CompleteCalls { get; } = [];

    /// <summary>One entry per <see cref="HandOverAsync"/> call, in order.</summary>
    public List<TransitionCall> HandOverCalls { get; } = [];

    /// <summary>One entry per <see cref="GetForProjectAsync"/> call, in order.</summary>
    public List<Guid> GetCalls { get; } = [];

    /// <summary>What <see cref="StartAsync"/> answers.</summary>
    public ConstructionTransitionResult NextStartResult { get; set; } =
        ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.DesignNotApproved);

    /// <summary>What <see cref="CompleteAsync"/> answers.</summary>
    public ConstructionTransitionResult NextCompleteResult { get; set; } =
        ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.NotStarted);

    /// <summary>What <see cref="HandOverAsync"/> answers.</summary>
    public ConstructionTransitionResult NextHandOverResult { get; set; } =
        ConstructionTransitionResult.Rejected(ConstructionTransitionOutcome.NotCompleted);

    /// <summary>What <see cref="GetForProjectAsync"/> answers. Null means construction has not started.</summary>
    public ConstructionPhase? NextPhase { get; set; }

    public Task<ConstructionTransitionResult> StartAsync(
        Guid projectId,
        Guid startedBy,
        CancellationToken cancellationToken = default)
    {
        StartCalls.Add(new TransitionCall(projectId, startedBy));
        return Task.FromResult(NextStartResult);
    }

    public Task<ConstructionTransitionResult> CompleteAsync(
        Guid projectId,
        Guid completedBy,
        CancellationToken cancellationToken = default)
    {
        CompleteCalls.Add(new TransitionCall(projectId, completedBy));
        return Task.FromResult(NextCompleteResult);
    }

    public Task<ConstructionTransitionResult> HandOverAsync(
        Guid projectId,
        Guid handedOverBy,
        CancellationToken cancellationToken = default)
    {
        HandOverCalls.Add(new TransitionCall(projectId, handedOverBy));
        return Task.FromResult(NextHandOverResult);
    }

    public Task<ConstructionPhase?> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        GetCalls.Add(projectId);
        return Task.FromResult(NextPhase);
    }

    /// <summary>One transition attempt: the project, and who asked for it.</summary>
    public readonly record struct TransitionCall(Guid ProjectId, Guid ActingUserId);
}
