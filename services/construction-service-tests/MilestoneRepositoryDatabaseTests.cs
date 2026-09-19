using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="MilestoneRepository"/> against real MySQL — the gate check
/// against <c>milestone_setups</c> that gives US-12 AC-1 its meaning, the
/// duplicate-key translation, and the aggregate query behind the AC-3
/// progress rollup.
/// </summary>
/// <remarks>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</remarks>
[Collection(ConstructionDatabaseCollection.Name)]
public class MilestoneRepositoryDatabaseTests
{
    private readonly ConstructionDatabaseFixture _fixture;

    public MilestoneRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task Creates_a_milestone_for_a_project_whose_design_has_been_approved()
    {
        var projectId = _fixture.ProjectId("b01");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");

        Assert.NotNull(milestone);
        Assert.NotEqual(Guid.Empty, milestone!.Id);
        Assert.Equal(projectId, milestone.ProjectId);
        Assert.Equal("Foundation poured", milestone.Name);
        // A fresh milestone is NotStarted — the DEFAULT on the column and
        // the initial value CreateAsync writes agree, so this pins both.
        Assert.Equal(MilestoneStatus.NotStarted, milestone.Status);
        Assert.NotEqual(default, milestone.CreatedAtUtc);
        Assert.Equal(milestone.CreatedAtUtc, milestone.UpdatedAtUtc);
    }

    [Fact]
    public async Task Returns_null_when_the_project_has_no_milestone_setups_row()
    {
        // AC-1: the DesignApproved event has not been consumed for this
        // project, so no placeholder row exists. The repository must refuse
        // the INSERT — a caller mapping this to 400 is the whole gate.
        var projectId = _fixture.ProjectId("b02");
        // Deliberately no PlantApprovedDesignAsync call.

        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");

        Assert.Null(milestone);

        // And the table stays empty for this project — the gate refusal
        // was not a race the INSERT still ran through.
        Assert.Empty(await _fixture.MilestoneRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task Throws_DuplicateMilestoneNameException_on_a_repeat_name_for_the_same_project()
    {
        var projectId = _fixture.ProjectId("b03");
        await _fixture.PlantApprovedDesignAsync(projectId);

        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");

        var exception = await Assert.ThrowsAsync<DuplicateMilestoneNameException>(
            () => _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured"));

        Assert.Equal(projectId, exception.ProjectId);
        Assert.Equal("Foundation poured", exception.Name);
    }

    [Fact]
    public async Task Allows_the_same_milestone_name_on_two_different_projects()
    {
        // The UNIQUE key is (project_id, name), not name alone: two projects
        // may both track a "Foundation poured" milestone.
        var projectOne = _fixture.ProjectId("b04");
        var projectTwo = _fixture.ProjectId("b05");
        await _fixture.PlantApprovedDesignAsync(projectOne);
        await _fixture.PlantApprovedDesignAsync(projectTwo);

        var one = await _fixture.MilestoneRepository.CreateAsync(projectOne, "Foundation poured");
        var two = await _fixture.MilestoneRepository.CreateAsync(projectTwo, "Foundation poured");

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotEqual(one!.Id, two!.Id);
    }

    // ---------- UpdateStatusAsync ----------

    [Fact]
    public async Task Moves_a_milestone_to_a_new_status_and_returns_the_updated_row()
    {
        var projectId = _fixture.ProjectId("b06");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");
        Assert.NotNull(created);

        var updated = await _fixture.MilestoneRepository.UpdateStatusAsync(
            created!.Id, MilestoneStatus.InProgress);

        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal(MilestoneStatus.InProgress, updated.Status);
        Assert.Equal(created.Name, updated.Name);
        // The status persisted — a fresh Read must see the same row.
        var reread = (await _fixture.MilestoneRepository.ListForProjectAsync(projectId))
            .Single(m => m.Id == created.Id);
        Assert.Equal(MilestoneStatus.InProgress, reread.Status);
    }

    [Fact]
    public async Task UpdateStatus_returns_null_when_no_milestone_has_that_id()
    {
        var unknownId = Guid.NewGuid();

        var updated = await _fixture.MilestoneRepository.UpdateStatusAsync(
            unknownId, MilestoneStatus.Completed);

        Assert.Null(updated);
    }

    // ---------- ListForProjectAsync ----------

    [Fact]
    public async Task Lists_milestones_for_a_project_oldest_first()
    {
        var projectId = _fixture.ProjectId("b07");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var first = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");
        var second = await _fixture.MilestoneRepository.CreateAsync(projectId, "Walls up");
        var third = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on");

        var rows = await _fixture.MilestoneRepository.ListForProjectAsync(projectId);

        Assert.Equal(3, rows.Count);
        Assert.Equal(first!.Id, rows[0].Id);
        Assert.Equal(second!.Id, rows[1].Id);
        Assert.Equal(third!.Id, rows[2].Id);
    }

    [Fact]
    public async Task Lists_only_milestones_of_the_given_project()
    {
        var projectA = _fixture.ProjectId("b08");
        var projectB = _fixture.ProjectId("b09");
        await _fixture.PlantApprovedDesignAsync(projectA);
        await _fixture.PlantApprovedDesignAsync(projectB);

        await _fixture.MilestoneRepository.CreateAsync(projectA, "A-Foundation");
        await _fixture.MilestoneRepository.CreateAsync(projectB, "B-Foundation");

        var rowsForA = await _fixture.MilestoneRepository.ListForProjectAsync(projectA);
        var rowsForB = await _fixture.MilestoneRepository.ListForProjectAsync(projectB);

        Assert.Equal("A-Foundation", Assert.Single(rowsForA).Name);
        Assert.Equal("B-Foundation", Assert.Single(rowsForB).Name);
    }

    // ---------- GetProgressForProjectAsync ----------

    [Fact]
    public async Task Progress_is_null_when_the_project_has_no_milestone_setups_row()
    {
        // AC-3: reporting "0%" for a project whose design has not been
        // approved would falsely suggest a plan exists. The controller maps
        // null to 404.
        var projectId = _fixture.ProjectId("b10");
        // Deliberately no PlantApprovedDesignAsync call.

        var progress = await _fixture.MilestoneRepository.GetProgressForProjectAsync(projectId);

        Assert.Null(progress);
    }

    [Fact]
    public async Task Progress_is_zero_of_zero_for_an_approved_project_with_no_milestones_yet()
    {
        // A real state: design approved, PM has not defined any milestones.
        // 0% is the honest answer.
        var projectId = _fixture.ProjectId("b11");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var progress = await _fixture.MilestoneRepository.GetProgressForProjectAsync(projectId);

        Assert.NotNull(progress);
        Assert.Equal(projectId, progress!.ProjectId);
        Assert.Equal(0, progress.TotalMilestones);
        Assert.Equal(0, progress.CompletedMilestones);
        Assert.Equal(0m, progress.ProgressPercent);
    }

    [Fact]
    public async Task Progress_reports_zero_percent_when_no_milestones_are_completed_yet()
    {
        var projectId = _fixture.ProjectId("b12");
        await _fixture.PlantApprovedDesignAsync(projectId);

        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Walls up");

        var progress = await _fixture.MilestoneRepository.GetProgressForProjectAsync(projectId);

        Assert.NotNull(progress);
        Assert.Equal(2, progress!.TotalMilestones);
        Assert.Equal(0, progress.CompletedMilestones);
        Assert.Equal(0m, progress.ProgressPercent);
    }

    [Fact]
    public async Task Progress_reports_the_percentage_for_a_partially_completed_project()
    {
        // AC-3: 1 of 3 is 33.33%, computed from the milestones on the fly.
        // Nothing stores the percent, so this is the aggregate query
        // answering directly.
        var projectId = _fixture.ProjectId("b13");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var one = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Walls up");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on");

        await _fixture.MilestoneRepository.UpdateStatusAsync(one!.Id, MilestoneStatus.Completed);

        var progress = await _fixture.MilestoneRepository.GetProgressForProjectAsync(projectId);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.TotalMilestones);
        Assert.Equal(1, progress.CompletedMilestones);
        Assert.Equal(33.33m, progress.ProgressPercent);
    }

    [Fact]
    public async Task Progress_reaches_a_hundred_percent_when_every_milestone_is_completed()
    {
        // AC-3 at the top end: every milestone Completed rolls the project
        // to 100.00%. A subsequent PATCH moving a row off Completed would
        // move the percentage down again — this is a snapshot, not a
        // one-way ratchet.
        var projectId = _fixture.ProjectId("b14");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var one = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");
        var two = await _fixture.MilestoneRepository.CreateAsync(projectId, "Walls up");

        await _fixture.MilestoneRepository.UpdateStatusAsync(one!.Id, MilestoneStatus.Completed);
        await _fixture.MilestoneRepository.UpdateStatusAsync(two!.Id, MilestoneStatus.Completed);

        var progress = await _fixture.MilestoneRepository.GetProgressForProjectAsync(projectId);

        Assert.NotNull(progress);
        Assert.Equal(2, progress!.TotalMilestones);
        Assert.Equal(2, progress.CompletedMilestones);
        Assert.Equal(100.00m, progress.ProgressPercent);
    }

    [Fact]
    public async Task Progress_ignores_InProgress_milestones_in_the_completed_count()
    {
        // Only 'Completed' contributes to the numerator — 'InProgress' is
        // work in flight, not work done. Pinned here because a well-meaning
        // future edit ("half-credit for in-progress") would change the
        // meaning of every stored answer.
        var projectId = _fixture.ProjectId("b15");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var one = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");
        var two = await _fixture.MilestoneRepository.CreateAsync(projectId, "Walls up");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on");

        await _fixture.MilestoneRepository.UpdateStatusAsync(one!.Id, MilestoneStatus.Completed);
        await _fixture.MilestoneRepository.UpdateStatusAsync(two!.Id, MilestoneStatus.InProgress);

        var progress = await _fixture.MilestoneRepository.GetProgressForProjectAsync(projectId);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.TotalMilestones);
        Assert.Equal(1, progress.CompletedMilestones);
        Assert.Equal(33.33m, progress.ProgressPercent);
    }
}