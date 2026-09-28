using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="ReportsController"/> over a stand-in repository: how the report rows reach
/// the wire, and that a portfolio with nothing to report is a real answer rather than an
/// error (US-19 AC-1).
/// </summary>
/// <remarks>
/// The role gating is pinned by <see cref="EndpointRoleDeclarationTests"/>; the
/// active-project gate and the aggregate counts by
/// <see cref="ConstructionReportRepositoryDatabaseTests"/>.
/// </remarks>
public class ReportsControllerTests
{
    private static readonly Guid FirstProject = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid SecondProject = Guid.Parse("22222222-0000-4000-8000-000000000002");

    [Fact]
    public async Task Returns_a_row_per_project_with_its_percentage_and_breakdown()
    {
        var repository = new FakeConstructionReportRepository
        {
            Rows =
            [
                Row(FirstProject, ConstructionPhaseStatus.Started, total: 4, completed: 1, inProgress: 2, notStarted: 1, percent: 25m),
                Row(SecondProject, ConstructionPhaseStatus.Completed, total: 2, completed: 2, inProgress: 0, notStarted: 0, percent: 100m)
            ]
        };

        var result = await new ReportsController(repository).GetProgressReport(default);

        var body = Assert.IsType<List<ConstructionProgressReportRowResponse>>(
            Assert.IsType<OkObjectResult>(result).Value);

        Assert.Equal(2, body.Count);

        var first = body[0];
        Assert.Equal(FirstProject, first.ProjectId);
        Assert.Equal(ConstructionPhaseStatus.Started, first.PhaseStatus);
        Assert.Equal(25m, first.ProgressPercent);
        // The breakdown rides alongside the percentage: one answers how far along, the
        // other what is actually outstanding.
        Assert.Equal(4, first.TotalMilestones);
        Assert.Equal(1, first.CompletedMilestones);
        Assert.Equal(2, first.InProgressMilestones);
        Assert.Equal(1, first.NotStartedMilestones);

        Assert.Equal(100m, body[1].ProgressPercent);
    }

    [Fact]
    public async Task A_project_whose_build_has_not_started_reports_a_null_phase()
    {
        // Milestones planned, nothing begun. A real line in the report — a PM needs to see
        // the build that has not started as much as the ones that have.
        var repository = new FakeConstructionReportRepository
        {
            Rows = [Row(FirstProject, phase: null, total: 3, completed: 0, inProgress: 0, notStarted: 3, percent: 0m)]
        };

        var result = await new ReportsController(repository).GetProgressReport(default);

        var row = Assert.Single(Assert.IsType<List<ConstructionProgressReportRowResponse>>(
            Assert.IsType<OkObjectResult>(result).Value));

        Assert.Null(row.PhaseStatus);
        Assert.Equal(0m, row.ProgressPercent);
        Assert.Equal(3, row.NotStartedMilestones);
    }

    [Fact]
    public async Task No_active_projects_with_milestones_is_an_empty_list_not_an_error()
    {
        // AC-1 is conditioned on projects having milestones defined. When none do, the
        // honest answer is "nothing to report" — a 404 or a 500 would have the caller
        // treating an empty portfolio as a fault.
        var repository = new FakeConstructionReportRepository { Rows = [] };

        var result = await new ReportsController(repository).GetProgressReport(default);

        var body = Assert.IsType<List<ConstructionProgressReportRowResponse>>(
            Assert.IsType<OkObjectResult>(result).Value);

        Assert.Empty(body);
        Assert.Equal(1, repository.CallCount);
    }

    private static ConstructionProgressReportRow Row(
        Guid projectId,
        ConstructionPhaseStatus? phase,
        int total,
        int completed,
        int inProgress,
        int notStarted,
        decimal percent) => new()
    {
        ProjectId = projectId,
        PhaseStatus = phase,
        TotalMilestones = total,
        CompletedMilestones = completed,
        InProgressMilestones = inProgress,
        NotStartedMilestones = notStarted,
        ProgressPercent = percent
    };
}
