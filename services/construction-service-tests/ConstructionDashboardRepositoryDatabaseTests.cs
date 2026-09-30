using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.ConstructionDashboardRepository"/> against real MySQL — the
/// ownership join behind a Client's progress, the "under way" gate behind a
/// Project Manager's active builds, and the outstanding-milestone count and list
/// (US-21).
/// </summary>
/// <remarks>
/// A stub cannot answer any of this. Each is a join across tables with a
/// NULL-preserving LEFT JOIN or a status gate in it, and whether the right
/// projects are included — and whether the counts add up — are questions only the
/// engine can settle.
/// <para>
/// Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.
/// The Client queries are narrowed by a Client id no other row carries. The
/// portfolio-wide ones cannot be, so they are read for this run's projects only, and
/// the outstanding total is asserted as a difference before and after.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class ConstructionDashboardRepositoryDatabaseTests
{
    /// <summary>The Project Manager the phase transitions are attributed to.</summary>
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private readonly ConstructionDatabaseFixture _fixture;

    /// <summary>A Client no other test or row shares.</summary>
    private readonly Guid _clientId = Guid.NewGuid();

    public ConstructionDashboardRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------------ Client's build progress ----

    [Fact]
    public async Task A_client_sees_only_the_projects_they_own()
    {
        var mine = _fixture.ProjectId("dda01");
        var someoneElses = _fixture.ProjectId("dda02");
        await PlantOwnedPlanAsync(mine, _clientId, ("Foundation", MilestoneStatus.NotStarted));
        await PlantOwnedPlanAsync(someoneElses, Guid.NewGuid(), ("Foundation", MilestoneStatus.NotStarted));

        var rows = await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId);

        Assert.Equal([mine], rows.Select(r => r.ProjectId));
    }

    [Fact]
    public async Task Reports_a_projects_percentage_and_milestone_breakdown_to_its_client()
    {
        var projectId = _fixture.ProjectId("dda03");
        await PlantOwnedPlanAsync(projectId, _clientId,
            ("Foundation", MilestoneStatus.Completed),
            ("Walls", MilestoneStatus.InProgress),
            ("Roof", MilestoneStatus.NotStarted),
            ("Finishing", MilestoneStatus.NotStarted));

        var row = Assert.Single(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));

        Assert.Equal(4, row.TotalMilestones);
        Assert.Equal(1, row.CompletedMilestones);
        Assert.Equal(1, row.InProgressMilestones);
        Assert.Equal(2, row.NotStartedMilestones);
        // One of four done, rounded the same way the per-project screen rounds it.
        Assert.Equal(25.00m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_planned_project_whose_build_has_not_started_is_shown_with_a_null_phase()
    {
        await PlantOwnedPlanAsync(_fixture.ProjectId("dda04"), _clientId, ("Foundation", MilestoneStatus.NotStarted));

        var row = Assert.Single(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));

        Assert.Null(row.PhaseStatus);
        Assert.Equal(0m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_running_build_is_shown_with_its_phase()
    {
        var projectId = _fixture.ProjectId("dda05");
        await PlantOwnedPlanAsync(projectId, _clientId, ("Foundation", MilestoneStatus.Completed));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var row = Assert.Single(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));

        Assert.Equal(ConstructionPhaseStatus.Started, row.PhaseStatus);
        Assert.Equal(100m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_project_with_no_milestones_is_left_out_of_a_clients_progress()
    {
        // Owned and design-approved, but nobody has planned the build. A 0% line would
        // read as a stalled build rather than an unplanned one.
        var projectId = _fixture.ProjectId("dda06");
        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, _clientId);
        await _fixture.PlantApprovedDesignAsync(projectId);

        Assert.Empty(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));
    }

    [Fact]
    public async Task A_handed_over_project_drops_out_of_a_clients_progress_but_a_completed_one_does_not()
    {
        var projectId = _fixture.ProjectId("dda07");
        await PlantOwnedPlanAsync(projectId, _clientId, ("Foundation", MilestoneStatus.Completed));
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        // Built but not yet delivered — still something to watch.
        var row = Assert.Single(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));
        Assert.Equal(ConstructionPhaseStatus.Completed, row.PhaseStatus);

        await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Empty(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));
    }

    [Fact]
    public async Task A_client_with_nothing_planned_gets_an_empty_list()
    {
        Assert.Empty(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));
    }

    // ------------------------------------------ Project Manager's active builds ----

    [Fact]
    public async Task A_started_build_is_an_active_build_with_its_breakdown()
    {
        var projectId = _fixture.ProjectId("dda10");
        await PlantOwnedPlanAsync(projectId, _clientId,
            ("Foundation", MilestoneStatus.Completed),
            ("Walls", MilestoneStatus.InProgress),
            ("Roof", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var row = Assert.Single(await ActiveBuildsForThisRunAsync(projectId));

        Assert.Equal(ConstructionPhaseStatus.Started, row.PhaseStatus);
        Assert.Equal(3, row.TotalMilestones);
        Assert.Equal(1, row.CompletedMilestones);
        Assert.Equal(1, row.InProgressMilestones);
        Assert.Equal(1, row.NotStartedMilestones);
        Assert.Equal(33.33m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_planned_build_nobody_has_started_is_not_an_active_build()
    {
        var projectId = _fixture.ProjectId("dda11");
        await PlantOwnedPlanAsync(projectId, _clientId, ("Foundation", MilestoneStatus.NotStarted));

        Assert.Empty(await ActiveBuildsForThisRunAsync(projectId));
    }

    [Fact]
    public async Task A_completed_build_is_still_active_until_it_is_handed_over()
    {
        var projectId = _fixture.ProjectId("dda12");
        await PlantOwnedPlanAsync(projectId, _clientId, ("Foundation", MilestoneStatus.Completed));
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(
            ConstructionPhaseStatus.Completed,
            Assert.Single(await ActiveBuildsForThisRunAsync(projectId)).PhaseStatus);

        await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Empty(await ActiveBuildsForThisRunAsync(projectId));
    }

    [Fact]
    public async Task Active_builds_are_across_every_client_not_one()
    {
        var first = _fixture.ProjectId("dda13");
        var second = _fixture.ProjectId("dda14");
        await PlantOwnedPlanAsync(first, _clientId, ("Foundation", MilestoneStatus.NotStarted));
        await PlantOwnedPlanAsync(second, Guid.NewGuid(), ("Foundation", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(first, ActingPm);
        await _fixture.ConstructionPhaseRepository.StartAsync(second, ActingPm);

        var rows = (await _fixture.ConstructionDashboardRepository.ListActiveBuildsAsync())
            .Where(r => r.ProjectId == first || r.ProjectId == second)
            .ToList();

        Assert.Equal(2, rows.Count);
    }

    // ------------------------------------------ Project Manager's milestones due ----

    [Fact]
    public async Task Outstanding_milestones_are_the_unfinished_ones_on_builds_that_have_started()
    {
        var before = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(1000);

        var running = _fixture.ProjectId("dda20");
        await PlantOwnedPlanAsync(running, _clientId,
            ("Foundation", MilestoneStatus.Completed),
            ("Walls", MilestoneStatus.InProgress),
            ("Roof", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(running, ActingPm);

        // Planned but never started: its milestones are not yet due.
        var planned = _fixture.ProjectId("dda21");
        await PlantOwnedPlanAsync(planned, _clientId, ("Foundation", MilestoneStatus.NotStarted));

        var after = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(1000);

        Assert.Equal(2, after.TotalCount - before.TotalCount);
        Assert.Equal(
            ["Walls", "Roof"],
            after.Items.Where(m => m.ProjectId == running).Select(m => m.Name));
        Assert.DoesNotContain(after.Items, m => m.ProjectId == planned);
    }

    [Fact]
    public async Task Milestones_already_in_progress_come_before_those_not_started()
    {
        var projectId = _fixture.ProjectId("dda22");
        // Planned in this order, so the not-started ones were created first.
        await PlantOwnedPlanAsync(projectId, _clientId,
            ("Roof", MilestoneStatus.NotStarted),
            ("Walls", MilestoneStatus.InProgress),
            ("Painting", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(1000);

        Assert.Equal(
            ["Walls", "Roof", "Painting"],
            outstanding.Items.Where(m => m.ProjectId == projectId).Select(m => m.Name));
    }

    [Fact]
    public async Task The_list_is_capped_but_the_total_is_not()
    {
        var projectId = _fixture.ProjectId("dda23");
        await PlantOwnedPlanAsync(projectId, _clientId,
            ("A", MilestoneStatus.InProgress),
            ("B", MilestoneStatus.NotStarted),
            ("C", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var everything = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(1000);
        var capped = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(2);

        Assert.Equal(2, capped.Items.Count);
        Assert.Equal(everything.TotalCount, capped.TotalCount);
        Assert.True(capped.TotalCount >= 3);
    }

    [Fact]
    public async Task A_finished_milestone_is_never_outstanding()
    {
        var projectId = _fixture.ProjectId("dda24");
        await PlantOwnedPlanAsync(projectId, _clientId, ("Foundation", MilestoneStatus.Completed));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(1000);

        Assert.DoesNotContain(outstanding.Items, m => m.ProjectId == projectId);
    }

    // ------------------------------------------------------------ helpers ----

    /// <summary>This run's active build for a project, if the query reports one.</summary>
    private async Task<IReadOnlyList<ConstructionProgressReportRow>> ActiveBuildsForThisRunAsync(Guid projectId) =>
        (await _fixture.ConstructionDashboardRepository.ListActiveBuildsAsync())
            .Where(row => row.ProjectId == projectId)
            .ToList();

    /// <summary>
    /// Records who owns the project, approves its design, and plants milestones on it
    /// in the order given, moving each to the status named.
    /// </summary>
    private async Task PlantOwnedPlanAsync(
        Guid projectId,
        Guid clientId,
        params (string Name, MilestoneStatus Status)[] milestones)
    {
        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId);
        await _fixture.PlantApprovedDesignAsync(projectId);

        foreach (var (name, status) in milestones)
        {
            var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, name);

            if (status is not MilestoneStatus.NotStarted)
            {
                await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, status);
            }
        }
    }
}
