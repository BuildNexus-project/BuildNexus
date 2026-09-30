using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.ConstructionReportRepository"/> against real MySQL — the
/// active-project gate and the breakdown counts the construction report is built from
/// (US-19 AC-1).
/// </summary>
/// <remarks>
/// A stub cannot answer any of this. The gate is a join across three tables with a NULL
/// -preserving LEFT JOIN in it, and the breakdown is three conditional SUMs over a
/// constrained status column — whether the right projects are included, and whether the
/// counts add up to the total, are questions only the engine can settle.
/// <para>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class ConstructionReportRepositoryDatabaseTests
{
    /// <summary>The Project Manager the phase transitions are attributed to.</summary>
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private readonly ConstructionDatabaseFixture _fixture;

    public ConstructionReportRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Reports_a_projects_percentage_and_milestone_breakdown()
    {
        var projectId = _fixture.ProjectId("ab1");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await PlantMilestonesAsync(projectId,
            ("Foundation", MilestoneStatus.Completed),
            ("Walls", MilestoneStatus.InProgress),
            ("Roof", MilestoneStatus.NotStarted),
            ("Finishing", MilestoneStatus.NotStarted));

        var row = await RowForAsync(projectId);

        Assert.NotNull(row);
        Assert.Equal(4, row.TotalMilestones);
        Assert.Equal(1, row.CompletedMilestones);
        Assert.Equal(1, row.InProgressMilestones);
        Assert.Equal(2, row.NotStartedMilestones);
        // One of four done — and rounded the same way the per-project screen rounds it.
        Assert.Equal(25.00m, row.ProgressPercent);
        // The three counts account for every milestone, which is what makes the breakdown
        // trustworthy rather than three unrelated numbers beside a total.
        Assert.Equal(
            row.TotalMilestones,
            row.CompletedMilestones + row.InProgressMilestones + row.NotStartedMilestones);
    }

    [Fact]
    public async Task A_project_whose_build_has_not_started_is_reported_with_a_null_phase()
    {
        // Design approved and milestones planned, but nobody has pressed start. A PM needs
        // to see this row — it is the build that has not begun.
        var projectId = _fixture.ProjectId("ab2");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await PlantMilestonesAsync(projectId, ("Foundation", MilestoneStatus.NotStarted));

        var row = await RowForAsync(projectId);

        Assert.NotNull(row);
        Assert.Null(row.PhaseStatus);
        Assert.Equal(0m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_running_build_is_reported_with_its_phase()
    {
        var projectId = _fixture.ProjectId("ab3");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await PlantMilestonesAsync(projectId, ("Foundation", MilestoneStatus.Completed));
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);

        var row = await RowForAsync(projectId);

        Assert.Equal(ConstructionPhaseStatus.Started, row!.PhaseStatus);
        Assert.Equal(100m, row.ProgressPercent);
    }

    [Fact]
    public async Task A_project_with_no_milestones_is_left_out_entirely()
    {
        // AC-1 is about projects that "have milestones defined". A 0% line for a project
        // nobody has planned yet would read as a stalled build rather than an unplanned one.
        var projectId = _fixture.ProjectId("ab4");
        await _fixture.PlantApprovedDesignAsync(projectId);

        Assert.Null(await RowForAsync(projectId));
    }

    [Fact]
    public async Task A_project_whose_design_is_not_approved_is_left_out()
    {
        // No milestone_setups row, so no build is planned at all. Such a project cannot
        // have milestones either — the gate that creates them is the same gate — but the
        // report leads on setups rather than trusting that, so the rule is enforced here
        // and not merely implied.
        var projectId = _fixture.ProjectId("ab5");

        Assert.Null(await RowForAsync(projectId));
    }

    [Fact]
    public async Task A_handed_over_project_drops_out_of_the_report()
    {
        // The other half of "active": a delivered build is finished, so it is no longer
        // something to track progress on. This is the one transition that removes a row.
        var projectId = _fixture.ProjectId("ab6");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await PlantMilestonesAsync(projectId, ("Foundation", MilestoneStatus.Completed));
        await _fixture.PlantSettledPaymentAsync(projectId);
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId, ActingPm);

        // Still reported while it is merely Completed — the build is done but not delivered.
        Assert.Equal(ConstructionPhaseStatus.Completed, (await RowForAsync(projectId))!.PhaseStatus);

        await _fixture.ConstructionPhaseRepository.HandOverAsync(projectId, ActingPm);

        Assert.Null(await RowForAsync(projectId));
    }

    [Fact]
    public async Task Rows_come_back_ordered_by_project_id()
    {
        // A stable order, so a report read twice does not reshuffle under the reader.
        var rows = await _fixture.ConstructionReportRepository.GetProgressAcrossActiveProjectsAsync();
        var ids = rows.Select(r => r.ProjectId).ToList();

        Assert.Equal(ids.OrderBy(id => id.ToString()).ToList(), ids);
    }

    /// <summary>This run's row for a project, or <c>null</c> when the report leaves it out.</summary>
    private async Task<ConstructionProgressReportRow?> RowForAsync(Guid projectId)
    {
        var rows = await _fixture.ConstructionReportRepository.GetProgressAcrossActiveProjectsAsync();

        return rows.SingleOrDefault(row => row.ProjectId == projectId);
    }

    /// <summary>Plants milestones on a project and moves each to the status named.</summary>
    private async Task PlantMilestonesAsync(
        Guid projectId,
        params (string Name, MilestoneStatus Status)[] milestones)
    {
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
