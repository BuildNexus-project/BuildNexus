using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.ConstructionDashboardRepository"/> against real MySQL — the ownership join
/// behind a Client's progress, the "under way" gate and project scoping behind a Project
/// Manager's active builds, and the outstanding, overdue and ordering rules for milestones
/// due (US-21).
/// </summary>
/// <remarks>
/// A stub cannot answer any of this. Each is a join across tables with a NULL-preserving LEFT
/// JOIN or a status gate in it, lateness is a comparison against a date that may be NULL, and
/// whether the right projects are included — and whether the counts add up — are questions
/// only the engine can settle.
/// <para>
/// Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>. Every
/// query is narrowed by a Client id or a set of project ids that only this test created, so
/// whatever else the development database holds cannot disturb the result and the totals can
/// be asserted as absolute numbers.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class ConstructionDashboardRepositoryDatabaseTests
{
    /// <summary>The Project Manager the phase transitions are attributed to.</summary>
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    /// <summary>
    /// The day lateness is judged against. Far from any real date, so a due date placed
    /// relative to it cannot be disturbed by the day the suite happens to run.
    /// </summary>
    private static readonly DateOnly Today = new(2031, 3, 10);

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
        await PlantOwnedPlanAsync(mine, _clientId, M("Foundation", MilestoneStatus.NotStarted));
        await PlantOwnedPlanAsync(someoneElses, Guid.NewGuid(), M("Foundation", MilestoneStatus.NotStarted));

        var rows = await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId);

        Assert.Equal([mine], rows.Select(r => r.ProjectId));
    }

    [Fact]
    public async Task Reports_a_projects_percentage_and_milestone_breakdown_to_its_client()
    {
        var projectId = _fixture.ProjectId("dda03");
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Foundation", MilestoneStatus.Completed),
            M("Walls", MilestoneStatus.InProgress),
            M("Roof", MilestoneStatus.NotStarted),
            M("Finishing", MilestoneStatus.NotStarted));

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
        await PlantOwnedPlanAsync(_fixture.ProjectId("dda04"), _clientId, M("Foundation", MilestoneStatus.NotStarted));

        var row = Assert.Single(await _fixture.ConstructionDashboardRepository.ListProgressForClientAsync(_clientId));

        Assert.Null(row.PhaseStatus);
        Assert.Equal(0m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_running_build_is_shown_with_its_phase()
    {
        var projectId = _fixture.ProjectId("dda05");
        await PlantOwnedPlanAsync(projectId, _clientId, M("Foundation", MilestoneStatus.Completed));
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
        await PlantOwnedPlanAsync(projectId, _clientId, M("Foundation", MilestoneStatus.Completed));
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
            M("Foundation", MilestoneStatus.Completed),
            M("Walls", MilestoneStatus.InProgress),
            M("Roof", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var row = Assert.Single(await ActiveBuildsAsync(projectId));

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
        await PlantOwnedPlanAsync(projectId, _clientId, M("Foundation", MilestoneStatus.NotStarted));

        Assert.Empty(await ActiveBuildsAsync(projectId));
    }

    [Fact]
    public async Task A_completed_build_is_still_active_until_it_is_handed_over()
    {
        var projectId = _fixture.ProjectId("dda12");
        await PlantOwnedPlanAsync(projectId, _clientId, M("Foundation", MilestoneStatus.Completed));
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        Assert.Equal(
            ConstructionPhaseStatus.Completed,
            Assert.Single(await ActiveBuildsAsync(projectId)).PhaseStatus);

        await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Empty(await ActiveBuildsAsync(projectId));
    }

    [Fact]
    public async Task Only_the_projects_asked_about_are_read()
    {
        // Two started builds, whoever owns them. Asking about one must not surface the other:
        // a Project Manager is only ever told about the projects they are assigned to.
        var first = _fixture.ProjectId("dda13");
        var second = _fixture.ProjectId("dda14");
        await PlantOwnedPlanAsync(first, _clientId, M("Foundation", MilestoneStatus.NotStarted));
        await PlantOwnedPlanAsync(second, Guid.NewGuid(), M("Foundation", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(first, ActingPm);
        await _fixture.ConstructionPhaseRepository.StartAsync(second, ActingPm);

        Assert.Equal([first], (await ActiveBuildsAsync(first)).Select(r => r.ProjectId));
        Assert.Equal(
            new[] { first, second }.Order(),
            (await ActiveBuildsAsync(first, second)).Select(r => r.ProjectId).Order());
    }

    [Fact]
    public async Task Asking_about_no_projects_answers_empty_without_error()
    {
        Assert.Empty(await _fixture.ConstructionDashboardRepository.ListActiveBuildsAsync([]));

        var outstanding = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync([], 10, Today);
        Assert.Equal(0, outstanding.TotalCount);
        Assert.Equal(0, outstanding.OverdueCount);
        Assert.Empty(outstanding.Items);
    }

    // ------------------------------------------ Project Manager's milestones due ----

    [Fact]
    public async Task Outstanding_milestones_are_the_unfinished_ones_on_builds_that_have_started()
    {
        var running = _fixture.ProjectId("dda20");
        await PlantOwnedPlanAsync(running, _clientId,
            M("Foundation", MilestoneStatus.Completed),
            M("Walls", MilestoneStatus.InProgress),
            M("Roof", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(running, ActingPm);

        // Planned but never started: its milestones are not yet due.
        var planned = _fixture.ProjectId("dda21");
        await PlantOwnedPlanAsync(planned, _clientId, M("Foundation", MilestoneStatus.NotStarted));

        var outstanding = await OutstandingAsync(1000, running, planned);

        Assert.Equal(2, outstanding.TotalCount);
        Assert.Equal(["Walls", "Roof"], outstanding.Items.Select(m => m.Name));
        Assert.DoesNotContain(outstanding.Items, m => m.ProjectId == planned);
    }

    [Fact]
    public async Task Undated_milestones_already_in_progress_come_before_those_not_started()
    {
        var projectId = _fixture.ProjectId("dda22");
        // Planned in this order, so the not-started ones were created first.
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Roof", MilestoneStatus.NotStarted),
            M("Walls", MilestoneStatus.InProgress),
            M("Painting", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await OutstandingAsync(1000, projectId);

        Assert.Equal(["Walls", "Roof", "Painting"], outstanding.Items.Select(m => m.Name));
    }

    [Fact]
    public async Task The_list_is_capped_but_the_total_is_not()
    {
        var projectId = _fixture.ProjectId("dda23");
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("A", MilestoneStatus.InProgress),
            M("B", MilestoneStatus.NotStarted),
            M("C", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var capped = await OutstandingAsync(2, projectId);

        Assert.Equal(2, capped.Items.Count);
        Assert.Equal(3, capped.TotalCount);
    }

    [Fact]
    public async Task A_finished_milestone_is_never_outstanding()
    {
        var projectId = _fixture.ProjectId("dda24");
        await PlantOwnedPlanAsync(projectId, _clientId, M("Foundation", MilestoneStatus.Completed));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await OutstandingAsync(1000, projectId);

        Assert.Equal(0, outstanding.TotalCount);
        Assert.Empty(outstanding.Items);
    }

    [Fact]
    public async Task Only_the_projects_asked_about_are_counted_as_outstanding()
    {
        var mine = _fixture.ProjectId("dda25");
        var theirs = _fixture.ProjectId("dda26");
        await PlantOwnedPlanAsync(mine, _clientId, M("Walls", MilestoneStatus.InProgress));
        await PlantOwnedPlanAsync(theirs, Guid.NewGuid(), M("Roof", MilestoneStatus.InProgress), M("Painting", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(mine, ActingPm);
        await _fixture.ConstructionPhaseRepository.StartAsync(theirs, ActingPm);

        var outstanding = await OutstandingAsync(1000, mine);

        Assert.Equal(1, outstanding.TotalCount);
        Assert.Equal(["Walls"], outstanding.Items.Select(m => m.Name));
    }

    // ------------------------------------------------------------ due dates ----

    [Fact]
    public async Task Dated_milestones_come_before_undated_ones_soonest_first()
    {
        var projectId = _fixture.ProjectId("ddb01");
        // Planned undated-first, so creation order alone would put the undated ones on top.
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Undated in progress", MilestoneStatus.InProgress),
            M("Undated not started", MilestoneStatus.NotStarted),
            M("Due later", MilestoneStatus.NotStarted, Today.AddDays(30)),
            M("Due soonest", MilestoneStatus.NotStarted, Today.AddDays(-5)),
            M("Due next", MilestoneStatus.NotStarted, Today.AddDays(2)));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await OutstandingAsync(1000, projectId);

        Assert.Equal(
            ["Due soonest", "Due next", "Due later", "Undated in progress", "Undated not started"],
            outstanding.Items.Select(m => m.Name));
    }

    [Fact]
    public async Task Within_one_due_date_those_already_in_progress_come_first()
    {
        var projectId = _fixture.ProjectId("ddb02");
        var sameDay = Today.AddDays(3);
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Not started", MilestoneStatus.NotStarted, sameDay),
            M("In progress", MilestoneStatus.InProgress, sameDay));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await OutstandingAsync(1000, projectId);

        Assert.Equal(["In progress", "Not started"], outstanding.Items.Select(m => m.Name));
    }

    [Fact]
    public async Task The_due_date_comes_back_with_each_milestone()
    {
        var projectId = _fixture.ProjectId("ddb03");
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Dated", MilestoneStatus.InProgress, new DateOnly(2031, 5, 17)),
            M("Undated", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var items = (await OutstandingAsync(1000, projectId)).Items;

        Assert.Equal(new DateOnly(2031, 5, 17), items.Single(m => m.Name == "Dated").DueDate);
        Assert.Null(items.Single(m => m.Name == "Undated").DueDate);
    }

    [Fact]
    public async Task A_milestone_is_overdue_only_when_its_due_date_is_before_today()
    {
        var projectId = _fixture.ProjectId("ddb04");
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Yesterday", MilestoneStatus.InProgress, Today.AddDays(-1)),
            M("Long ago", MilestoneStatus.NotStarted, Today.AddDays(-90)),
            M("Today", MilestoneStatus.InProgress, Today),
            M("Tomorrow", MilestoneStatus.NotStarted, Today.AddDays(1)),
            M("Undated", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await OutstandingAsync(1000, projectId);

        // Two are late. The one due today is not late yet, and a milestone with no date
        // is outstanding but can never be.
        Assert.Equal(5, outstanding.TotalCount);
        Assert.Equal(2, outstanding.OverdueCount);
    }

    [Fact]
    public async Task A_finished_milestone_is_not_overdue_however_old_its_due_date()
    {
        var projectId = _fixture.ProjectId("ddb05");
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Done long ago", MilestoneStatus.Completed, Today.AddDays(-400)),
            M("Still to do", MilestoneStatus.NotStarted));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var outstanding = await OutstandingAsync(1000, projectId);

        Assert.Equal(1, outstanding.TotalCount);
        Assert.Equal(0, outstanding.OverdueCount);
    }

    [Fact]
    public async Task Overdue_is_judged_against_the_day_the_caller_names()
    {
        var projectId = _fixture.ProjectId("ddb06");
        await PlantOwnedPlanAsync(projectId, _clientId, M("Due on the tenth", MilestoneStatus.InProgress, Today));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var onTheDay = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync([projectId], 10, Today);
        var nextDay = await _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync([projectId], 10, Today.AddDays(1));

        Assert.Equal(0, onTheDay.OverdueCount);
        Assert.Equal(1, nextDay.OverdueCount);
    }

    [Fact]
    public async Task The_overdue_count_is_not_limited_by_the_cap_on_the_list()
    {
        var projectId = _fixture.ProjectId("ddb07");
        await PlantOwnedPlanAsync(projectId, _clientId,
            M("Late A", MilestoneStatus.InProgress, Today.AddDays(-3)),
            M("Late B", MilestoneStatus.NotStarted, Today.AddDays(-2)),
            M("Late C", MilestoneStatus.NotStarted, Today.AddDays(-1)));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var capped = await OutstandingAsync(1, projectId);

        Assert.Single(capped.Items);
        Assert.Equal(3, capped.TotalCount);
        Assert.Equal(3, capped.OverdueCount);
    }

    // ------------------------------------------------------------ helpers ----

    private async Task<IReadOnlyList<ConstructionProgressReportRow>> ActiveBuildsAsync(params Guid[] projectIds) =>
        await _fixture.ConstructionDashboardRepository.ListActiveBuildsAsync(projectIds);

    private Task<OutstandingMilestones> OutstandingAsync(int limit, params Guid[] projectIds) =>
        _fixture.ConstructionDashboardRepository.GetOutstandingMilestonesAsync(projectIds, limit, Today);

    /// <summary>One milestone to plant: its name, the status to move it to, and an optional due date.</summary>
    private static (string Name, MilestoneStatus Status, DateOnly? DueDate) M(
        string name,
        MilestoneStatus status,
        DateOnly? dueDate = null) => (name, status, dueDate);

    /// <summary>
    /// Records who owns the project, approves its design, and plants milestones on it in the
    /// order given, moving each to the status named.
    /// </summary>
    private async Task PlantOwnedPlanAsync(
        Guid projectId,
        Guid clientId,
        params (string Name, MilestoneStatus Status, DateOnly? DueDate)[] milestones)
    {
        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId);
        await _fixture.PlantApprovedDesignAsync(projectId);

        foreach (var (name, status, dueDate) in milestones)
        {
            var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, name, dueDate);

            if (status is not MilestoneStatus.NotStarted)
            {
                await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, status);
            }
        }
    }
}
