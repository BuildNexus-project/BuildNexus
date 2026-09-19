using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IMilestoneRepository"/> that records the calls made to it, so
/// the <see cref="Controllers.MilestonesController"/> suite can check behaviour
/// without MySQL. The real ADO.NET SQL — the gate check, the UPDATE-then-SELECT,
/// the aggregate query — is covered by the database-backed suite in a later
/// commit.
/// </summary>
public sealed class FakeMilestoneRepository : IMilestoneRepository
{
    /// <summary>One entry per <see cref="CreateAsync"/> call, in order.</summary>
    public List<CreateCall> CreateCalls { get; } = [];

    /// <summary>What <see cref="CreateAsync"/> returns when it does not throw. Null simulates the design-approval gate refusing.</summary>
    public Milestone? NextCreatedMilestone { get; set; }

    /// <summary>When set, <see cref="CreateAsync"/> throws it — a stand-in for the duplicate-name conflict.</summary>
    public Exception? CreateThrows { get; set; }

    /// <summary>One entry per <see cref="UpdateStatusAsync"/> call, in order.</summary>
    public List<UpdateStatusCall> UpdateStatusCalls { get; } = [];

    /// <summary>What <see cref="UpdateStatusAsync"/> returns. Null simulates the milestone id being unknown.</summary>
    public Milestone? NextUpdatedMilestone { get; set; }

    /// <summary>Rows <see cref="ListForProjectAsync"/> returns.</summary>
    public IReadOnlyList<Milestone> ListRows { get; set; } = [];

    /// <summary>What <see cref="GetProgressForProjectAsync"/> returns. Null simulates the project's design not being approved.</summary>
    public ProjectProgress? NextProgress { get; set; }

    public Task<Milestone?> CreateAsync(
        Guid projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        CreateCalls.Add(new CreateCall(projectId, name));

        if (CreateThrows is not null)
        {
            throw CreateThrows;
        }

        return Task.FromResult(NextCreatedMilestone);
    }

    public Task<Milestone?> UpdateStatusAsync(
        Guid milestoneId,
        MilestoneStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        UpdateStatusCalls.Add(new UpdateStatusCall(milestoneId, newStatus));
        return Task.FromResult(NextUpdatedMilestone);
    }

    public Task<IReadOnlyList<Milestone>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ListRows);

    public Task<ProjectProgress?> GetProgressForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NextProgress);

    public readonly record struct CreateCall(Guid ProjectId, string Name);

    public readonly record struct UpdateStatusCall(Guid MilestoneId, MilestoneStatus NewStatus);
}