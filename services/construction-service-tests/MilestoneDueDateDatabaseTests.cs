using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// A milestone's optional due date (US-21) against real MySQL: stored as a calendar day,
/// read back as the same day, settable and clearable afterwards, and never disturbing how a
/// milestone without one behaves.
/// </summary>
/// <remarks>
/// The column is a <c>DATE</c>, so the one thing only the engine can answer is that a
/// <see cref="DateOnly"/> goes in and comes out as the very same day — with no time of day to
/// drift it a day either way across a timezone. Needs <c>construction-db</c> running — see
/// <see cref="ConstructionDatabaseFixture"/>.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class MilestoneDueDateDatabaseTests
{
    private static readonly DateOnly Friday = new(2031, 5, 16);

    private readonly ConstructionDatabaseFixture _fixture;

    public MilestoneDueDateDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_milestone_created_with_a_due_date_keeps_it()
    {
        var projectId = _fixture.ProjectId("ddc01");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured", Friday);

        Assert.Equal(Friday, created!.DueDate);

        // Read back from the table, not just echoed from what was written.
        var listed = Assert.Single(await _fixture.MilestoneRepository.ListForProjectAsync(projectId));
        Assert.Equal(Friday, listed.DueDate);
    }

    [Fact]
    public async Task A_milestone_created_without_one_has_none_and_behaves_as_before()
    {
        var projectId = _fixture.ProjectId("ddc02");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation poured");

        Assert.Null(created!.DueDate);
        Assert.Null(Assert.Single(await _fixture.MilestoneRepository.ListForProjectAsync(projectId)).DueDate);
        Assert.Equal(MilestoneStatus.NotStarted, created.Status);
    }

    [Fact]
    public async Task The_day_survives_the_round_trip_exactly_with_no_time_or_timezone_to_shift_it()
    {
        // The last day of a month and the first of the next: the two days a one-day drift
        // across a timezone would land on the wrong side of.
        var projectId = _fixture.ProjectId("ddc03");
        await _fixture.PlantApprovedDesignAsync(projectId);

        await _fixture.MilestoneRepository.CreateAsync(projectId, "End of month", new DateOnly(2031, 1, 31));
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Start of month", new DateOnly(2031, 2, 1));

        var listed = await _fixture.MilestoneRepository.ListForProjectAsync(projectId);

        Assert.Equal(new DateOnly(2031, 1, 31), listed.Single(m => m.Name == "End of month").DueDate);
        Assert.Equal(new DateOnly(2031, 2, 1), listed.Single(m => m.Name == "Start of month").DueDate);
    }

    [Fact]
    public async Task A_due_date_can_be_set_on_a_milestone_that_had_none()
    {
        var projectId = _fixture.ProjectId("ddc04");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on");

        var updated = await _fixture.MilestoneRepository.SetDueDateAsync(created!.Id, Friday);

        Assert.Equal(Friday, updated!.DueDate);
        Assert.Equal(Friday, Assert.Single(await _fixture.MilestoneRepository.ListForProjectAsync(projectId)).DueDate);
    }

    [Fact]
    public async Task A_due_date_can_be_changed()
    {
        var projectId = _fixture.ProjectId("ddc05");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on", Friday);

        var updated = await _fixture.MilestoneRepository.SetDueDateAsync(created!.Id, Friday.AddDays(10));

        Assert.Equal(Friday.AddDays(10), updated!.DueDate);
    }

    [Fact]
    public async Task A_due_date_can_be_cleared()
    {
        var projectId = _fixture.ProjectId("ddc06");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on", Friday);

        var updated = await _fixture.MilestoneRepository.SetDueDateAsync(created!.Id, null);

        Assert.Null(updated!.DueDate);
        Assert.Null(Assert.Single(await _fixture.MilestoneRepository.ListForProjectAsync(projectId)).DueDate);
    }

    [Fact]
    public async Task Setting_a_due_date_on_a_milestone_that_does_not_exist_answers_null()
    {
        Assert.Null(await _fixture.MilestoneRepository.SetDueDateAsync(Guid.NewGuid(), Friday));
    }

    [Fact]
    public async Task Setting_a_due_date_moves_updated_at_and_nothing_else_about_the_milestone()
    {
        var projectId = _fixture.ProjectId("ddc07");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on");
        await _fixture.MilestoneRepository.UpdateStatusAsync(created!.Id, MilestoneStatus.InProgress);

        // Compared against what the table held, not the object CreateAsync returned: the
        // column keeps microseconds, so a timestamp held in memory is a few ticks finer.
        var before = Assert.Single(await _fixture.MilestoneRepository.ListForProjectAsync(projectId));

        var updated = await _fixture.MilestoneRepository.SetDueDateAsync(created.Id, Friday);

        Assert.Equal("Roof on", updated!.Name);
        Assert.Equal(MilestoneStatus.InProgress, updated.Status);
        Assert.True(updated.UpdatedAtUtc >= before.UpdatedAtUtc);
        Assert.Equal(before.CreatedAtUtc, updated.CreatedAtUtc);
    }

    [Fact]
    public async Task Moving_a_milestone_to_a_new_status_keeps_its_due_date()
    {
        var projectId = _fixture.ProjectId("ddc08");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on", Friday);

        var moved = await _fixture.MilestoneRepository.UpdateStatusAsync(created!.Id, MilestoneStatus.Completed);

        Assert.Equal(Friday, moved!.DueDate);
    }

    [Fact]
    public async Task A_completed_milestone_may_still_be_given_a_date()
    {
        var projectId = _fixture.ProjectId("ddc09");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var created = await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof on");
        await _fixture.MilestoneRepository.UpdateStatusAsync(created!.Id, MilestoneStatus.Completed);

        var updated = await _fixture.MilestoneRepository.SetDueDateAsync(created.Id, Friday);

        Assert.Equal(Friday, updated!.DueDate);
        Assert.Equal(MilestoneStatus.Completed, updated.Status);
    }

    [Fact]
    public async Task Milestones_planted_from_the_template_have_no_date_until_they_are_given_one()
    {
        var projectId = _fixture.ProjectId("ddc10");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var planted = await _fixture.MilestoneRepository.CreateFromTemplateAsync(projectId, MilestoneTemplates.Canonical);

        Assert.All(planted!, milestone => Assert.Null(milestone.DueDate));

        var first = planted!.First();
        var dated = await _fixture.MilestoneRepository.SetDueDateAsync(first.Id, Friday);

        Assert.Equal(Friday, dated!.DueDate);
    }
}
