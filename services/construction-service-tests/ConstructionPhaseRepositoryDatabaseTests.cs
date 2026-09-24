using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.ConstructionPhaseRepository"/> against real MySQL — the three
/// gated transitions US-14 defines, and every precondition that refuses one
/// (AC-3).
/// </summary>
/// <remarks>
/// A stub is not enough for these. Each gate is a question answered by SQL
/// reading another table inside the transaction that writes — the
/// <c>milestone_setups</c> marker, the milestone tally — and the start gate's
/// "already started" answer comes from the primary key rejecting a second row
/// rather than from a check in C#. Only the real engine can show any of that.
/// <para>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</para>
/// </remarks>
[Collection(ConstructionDatabaseCollection.Name)]
public class ConstructionPhaseRepositoryDatabaseTests
{
    /// <summary>
    /// The Project Manager standing in for the caller. Not read by any gate — it
    /// travels onto the event so the Project Service can attribute the status
    /// change it makes in reaction — so one value serves every test here.
    /// </summary>
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private readonly ConstructionDatabaseFixture _fixture;

    public ConstructionPhaseRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------------------------- start ----

    [Fact]
    public async Task Start_records_the_phase_when_the_design_is_approved_and_milestones_exist()
    {
        var projectId = _fixture.ProjectId("d01");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        var result = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Phase);
        Assert.Equal(ConstructionPhaseStatus.Started, result.Phase.Status);
        Assert.Null(result.Phase.CompletedAtUtc);
        Assert.Null(result.Phase.HandedOverAtUtc);

        // And it is readable back out of the table, stamped as UTC.
        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.NotNull(stored);
        Assert.Equal(ConstructionPhaseStatus.Started, stored.Status);
        Assert.Equal(DateTimeKind.Utc, stored.StartedAtUtc.Kind);
    }

    [Fact]
    public async Task Start_is_refused_when_the_design_is_not_approved()
    {
        // No milestone_setups row: the project's DesignApproved event has not been
        // consumed, so as far as this service knows its design is not signed off.
        var projectId = _fixture.ProjectId("d02");

        var result = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.DesignNotApproved, result.Outcome);
        Assert.Null(result.Phase);
        Assert.Null(await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId));
    }

    [Fact]
    public async Task Start_is_refused_when_the_project_has_no_milestones()
    {
        // The two start preconditions are independent: an approved design with no
        // plan behind it must be refused on its own terms, and with its own reason
        // — not folded into "design not approved", which would be untrue here.
        var projectId = _fixture.ProjectId("d03");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var result = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.NoMilestonesDefined, result.Outcome);
        Assert.Null(await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId));
    }

    [Fact]
    public async Task Start_is_refused_a_second_time()
    {
        var projectId = _fixture.ProjectId("d04");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        var first = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        var second = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.Succeeded, first.Outcome);
        Assert.Equal(ConstructionTransitionOutcome.AlreadyStarted, second.Outcome);

        // And the refusal left the original row exactly as it was — the second
        // attempt must not restamp started_at.
        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.NotNull(stored);
        Assert.Equal(first.Phase!.StartedAtUtc, stored.StartedAtUtc, TimeSpan.FromMilliseconds(1));
    }

    // ---------------------------------------------------------- complete ----

    [Fact]
    public async Task Complete_marks_the_phase_when_every_milestone_is_completed()
    {
        var projectId = _fixture.ProjectId("d05");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation", "Roof");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var result = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Phase);
        Assert.Equal(ConstructionPhaseStatus.Completed, result.Phase.Status);
        Assert.NotNull(result.Phase.CompletedAtUtc);
        // started_at is carried through from the row rather than restamped.
        Assert.True(result.Phase.StartedAtUtc <= result.Phase.CompletedAtUtc);

        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.NotNull(stored);
        Assert.Equal(ConstructionPhaseStatus.Completed, stored.Status);
        Assert.Equal(DateTimeKind.Utc, stored.CompletedAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Complete_is_refused_when_construction_was_never_started()
    {
        // AC-3 names this case explicitly: the milestones can all be done and the
        // transition is still invalid, because there is no build to finish.
        var projectId = _fixture.ProjectId("d06");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");

        var result = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.NotStarted, result.Outcome);
        Assert.Null(await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId));
    }

    [Fact]
    public async Task Complete_is_refused_while_a_milestone_is_unfinished()
    {
        var projectId = _fixture.ProjectId("d07");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var done = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");
        await _fixture.MilestoneRepository.UpdateStatusAsync(done!.Id, MilestoneStatus.Completed);
        // Left InProgress — one unfinished milestone is enough to refuse.
        var pending = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof");
        await _fixture.MilestoneRepository.UpdateStatusAsync(pending!.Id, MilestoneStatus.InProgress);

        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var result = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.MilestonesIncomplete, result.Outcome);

        // The phase is untouched — still Started, still no completed_at.
        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.NotNull(stored);
        Assert.Equal(ConstructionPhaseStatus.Started, stored.Status);
        Assert.Null(stored.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_is_refused_a_second_time()
    {
        var projectId = _fixture.ProjectId("d08");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var first = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);
        var second = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.Succeeded, first.Outcome);
        Assert.Equal(ConstructionTransitionOutcome.AlreadyCompleted, second.Outcome);

        // completed_at is not restamped by the refused attempt.
        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.Equal(
            first.Phase!.CompletedAtUtc!.Value,
            stored!.CompletedAtUtc!.Value,
            TimeSpan.FromMilliseconds(1));
    }


    // ---------------------------------------------------------- handover ----

    [Fact]
    public async Task Handover_moves_a_completed_and_paid_project_to_its_terminal_state()
    {
        var projectId = _fixture.ProjectId("d09");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        var result = await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Phase);
        Assert.Equal(ConstructionPhaseStatus.HandedOver, result.Phase.Status);
        Assert.NotNull(result.Phase.HandedOverAtUtc);
        // Handover raises no event, so this column is the only record of who ended the
        // project.
        Assert.Equal(ActingPm, result.Phase.HandedOverByUserId);
        // The earlier stages are carried through, not overwritten — the summary the
        // Client reads needs the whole span.
        Assert.NotNull(result.Phase.CompletedAtUtc);

        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.NotNull(stored);
        Assert.Equal(ConstructionPhaseStatus.HandedOver, stored.Status);
        Assert.Equal(ActingPm, stored.HandedOverByUserId);
        Assert.Equal(DateTimeKind.Utc, stored.HandedOverAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Handover_is_refused_when_construction_was_never_started()
    {
        var projectId = _fixture.ProjectId("d10");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.PlantSettledPaymentAsync(projectId);

        var result = await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.NotStarted, result.Outcome);
        Assert.Null(await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId));
    }

    [Fact]
    public async Task Handover_is_refused_while_the_build_is_still_running()
    {
        // Paid up front, but the build is not finished. AC-4 hands over a finished
        // project, and "still running" is its own answer rather than being folded into
        // "never started" — a different thing for the PM to be told.
        var projectId = _fixture.ProjectId("d11");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var result = await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.NotCompleted, result.Outcome);

        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.Equal(ConstructionPhaseStatus.Started, stored!.Status);
        Assert.Null(stored.HandedOverAtUtc);
        Assert.Null(stored.HandedOverByUserId);
    }

    [Fact]
    public async Task Handover_is_refused_when_the_final_payment_is_not_settled()
    {
        // The build is finished, but no settlement marker exists — which is also the
        // state every project is in while the Payment Service is unbuilt. Refusing is
        // the safe direction: a project held back can be handed over once the marker
        // arrives, whereas one handed over unpaid cannot be un-handed.
        var projectId = _fixture.ProjectId("d12");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        var result = await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.FinalPaymentNotSettled, result.Outcome);

        // Still Completed, and nothing about handover was written.
        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.Equal(ConstructionPhaseStatus.Completed, stored!.Status);
        Assert.Null(stored.HandedOverAtUtc);
    }

    [Fact]
    public async Task Another_projects_settlement_does_not_let_this_one_be_handed_over()
    {
        // The gate must answer this project rather than "some settlement exists" — the
        // mistake there would hand over every finished project the moment any one of
        // them was paid for.
        var unpaid = _fixture.ProjectId("d13");
        await _fixture.PlantSettledPaymentAsync(_fixture.ProjectId("d14"));

        await _fixture.PlantApprovedDesignAsync(unpaid);
        await CompleteEveryMilestoneAsync(unpaid, "Foundation");
        await _fixture.ConstructionPhaseRepository.StartAsync(unpaid, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(unpaid, ActingPm);

        var result = await _fixture.ConstructionPhaseRepository.HandOverAsync(unpaid, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.FinalPaymentNotSettled, result.Outcome);
    }

    [Fact]
    public async Task Handover_is_refused_a_second_time_and_the_state_is_terminal()
    {
        var projectId = _fixture.ProjectId("d15");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        var first = await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);
        var second = await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.Succeeded, first.Outcome);
        Assert.Equal(ConstructionTransitionOutcome.AlreadyHandedOver, second.Outcome);

        // handed_over_at is not restamped by the refused attempt.
        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.Equal(
            first.Phase!.HandedOverAtUtc!.Value,
            stored!.HandedOverAtUtc!.Value,
            TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Nothing_can_leave_the_handed_over_state()
    {
        // Terminal means terminal: the earlier transitions must refuse it too, or a
        // finished-and-delivered project could be reopened by pressing complete again.
        var projectId = _fixture.ProjectId("d16");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        var restart = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        var recomplete = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(ConstructionTransitionOutcome.AlreadyStarted, restart.Outcome);
        Assert.Equal(ConstructionTransitionOutcome.AlreadyCompleted, recomplete.Outcome);

        var stored = await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId);
        Assert.Equal(ConstructionPhaseStatus.HandedOver, stored!.Status);
    }

    /// <summary>
    /// Plants the named milestones on the project and moves every one of them to
    /// <see cref="MilestoneStatus.Completed"/> — the state AC-2 requires before
    /// construction may be marked complete.
    /// </summary>
    private async Task CompleteEveryMilestoneAsync(Guid projectId, params string[] names)
    {
        foreach (var name in names)
        {
            var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, name);
            await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, MilestoneStatus.Completed);
        }
    }
}
