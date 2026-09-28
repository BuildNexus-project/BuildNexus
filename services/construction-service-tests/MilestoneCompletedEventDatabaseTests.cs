using BuildNexus.ConstructionService.Messaging;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The <c>MilestoneCompleted</c> event US-24 adds, read back off
/// <c>construction_outbox_events</c> after a real status change.
/// </summary>
/// <remarks>
/// Only the real engine can answer the two things that matter here. The event is enqueued in
/// the same transaction as the status change, so "a non-completing update announces nothing"
/// is a claim about what the table holds afterwards; and the event must be writable for a
/// project whose build has not started, which is exactly the foreign key migration 009
/// removes.
/// <para>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class MilestoneCompletedEventDatabaseTests
{
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private readonly ConstructionDatabaseFixture _fixture;

    public MilestoneCompletedEventDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Completing_a_milestone_announces_it_with_its_name_and_project()
    {
        var projectId = _fixture.ProjectId("ba1");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, MilestoneStatus.Completed);

        var announced = Assert.Single(
            await _fixture.OutboxRepository.ListForProjectAsync(projectId));

        Assert.Equal(ConstructionEventTypes.MilestoneCompleted, announced.EventType);
        Assert.False(announced.IsPublished);

        using var document = System.Text.Json.JsonDocument.Parse(announced.Envelope);
        var root = document.RootElement;

        Assert.Equal("MilestoneCompleted", root.GetProperty("eventType").GetString());
        Assert.Equal(announced.Id, root.GetProperty("eventId").GetGuid());

        var payload = root.GetProperty("payload");
        Assert.Equal(milestone.Id, payload.GetProperty("milestoneId").GetGuid());
        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        // The name travels too: a consumer cannot resolve a milestone id against this
        // service's database and should not be invited to try.
        Assert.Equal("Foundation", payload.GetProperty("name").GetString());
        Assert.Equal(TimeSpan.Zero, payload.GetProperty("completedAt").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task A_milestone_can_be_completed_before_the_build_has_started()
    {
        // The case migration 009's dropped foreign key exists for. With the key in place this
        // enqueue fails, and since it shares the status change's transaction it would roll the
        // Project Manager's update back — completing the first milestone before pressing Start
        // would simply error.
        var projectId = _fixture.ProjectId("ba2");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        // Deliberately no StartAsync — there is no construction_phases row at all.
        Assert.Null(await _fixture.ConstructionPhaseRepository.GetForProjectAsync(projectId));

        var updated = await _fixture.MilestoneRepository.UpdateStatusAsync(
            milestone!.Id, MilestoneStatus.Completed);

        Assert.NotNull(updated);
        Assert.Equal(MilestoneStatus.Completed, updated.Status);
        Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
    }

    [Theory]
    [InlineData(MilestoneStatus.NotStarted)]
    [InlineData(MilestoneStatus.InProgress)]
    public async Task Moving_a_milestone_to_any_other_status_announces_nothing(MilestoneStatus target)
    {
        var projectId = _fixture.ProjectId(target is MilestoneStatus.NotStarted ? "ba3" : "ba4");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, target);

        Assert.Empty(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task Completing_an_already_completed_milestone_announces_nothing_a_second_time()
    {
        // A repeat PATCH is not a milestone finishing. Announcing it again would put two
        // events on the topic for one piece of work, and a consumer counting completions would
        // double-count.
        var projectId = _fixture.ProjectId("ba5");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, MilestoneStatus.Completed);
        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone.Id, MilestoneStatus.Completed);

        Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task Re_completing_a_milestone_that_was_moved_back_announces_it_again()
    {
        // Genuinely a second completion: the work was reopened and finished again, so the topic
        // should say so. This is the other side of the transition rule — it suppresses repeats,
        // not real events.
        var projectId = _fixture.ProjectId("ba6");
        await _fixture.PlantApprovedDesignAsync(projectId);
        var milestone = await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone!.Id, MilestoneStatus.Completed);
        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone.Id, MilestoneStatus.InProgress);
        await _fixture.MilestoneRepository.UpdateStatusAsync(milestone.Id, MilestoneStatus.Completed);

        var events = await _fixture.OutboxRepository.ListForProjectAsync(projectId);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(ConstructionEventTypes.MilestoneCompleted, e.EventType));
    }

    [Fact]
    public async Task An_unknown_milestone_id_still_answers_null_and_announces_nothing()
    {
        var projectId = _fixture.ProjectId("ba7");
        await _fixture.PlantApprovedDesignAsync(projectId);

        Assert.Null(await _fixture.MilestoneRepository.UpdateStatusAsync(
            Guid.NewGuid(), MilestoneStatus.Completed));

        Assert.Empty(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
    }
}
