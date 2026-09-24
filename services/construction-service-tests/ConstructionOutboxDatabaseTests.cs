using System.Text.Json;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The events US-14's transitions announce, read back off
/// <c>construction_outbox_events</c> — that a successful transition enqueued
/// exactly one event with the agreed envelope, and that a refused one announced
/// nothing at all.
/// </summary>
/// <remarks>
/// The second half is the point. The event is written inside the transition's own
/// transaction, so "a rejected start publishes nothing" is a claim about a rollback
/// — and only a real engine can be asked whether the row survived.
/// <para>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</para>
/// </remarks>
[Collection(ConstructionDatabaseCollection.Name)]
public class ConstructionOutboxDatabaseTests
{
    private readonly ConstructionDatabaseFixture _fixture;

    public ConstructionOutboxDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Starting_construction_enqueues_one_ConstructionStarted()
    {
        var projectId = _fixture.ProjectId("e01");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof");

        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        var events = await _fixture.OutboxRepository.ListForProjectAsync(projectId);

        var announced = Assert.Single(events);
        Assert.Equal(ConstructionEventTypes.ConstructionStarted, announced.EventType);
        // Nothing has tried to send it yet — the dispatcher is not running here.
        Assert.False(announced.IsPublished);
        Assert.Equal(0, announced.AttemptCount);
        Assert.Null(announced.LastError);
    }

    [Fact]
    public async Task The_ConstructionStarted_envelope_matches_the_shared_shape()
    {
        var projectId = _fixture.ProjectId("e02");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof");

        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        var announced = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));

        // The envelope is a contract with four other services, so it is asserted as
        // JSON rather than through a typed round trip — a renamed property would
        // survive the latter and break every consumer.
        using var document = JsonDocument.Parse(announced.Envelope);
        var root = document.RootElement;

        Assert.Equal("ConstructionStarted", root.GetProperty("eventType").GetString());
        // The row and the envelope share the id, so a message on the topic can be
        // traced back to the row that produced it.
        Assert.Equal(announced.Id, root.GetProperty("eventId").GetGuid());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("payload").ValueKind);

        // occurredAt must carry an offset: a consumer reading a bare local-looking
        // timestamp would place the event in its own timezone.
        var occurredAt = root.GetProperty("occurredAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, occurredAt.Offset);

        var payload = root.GetProperty("payload");
        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(2, payload.GetProperty("milestoneCount").GetInt32());
        Assert.Equal(TimeSpan.Zero, payload.GetProperty("startedAt").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task Completing_construction_enqueues_ConstructionCompleted_after_the_start()
    {
        var projectId = _fixture.ProjectId("e03");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation", "Roof", "Finishing");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId);

        var events = await _fixture.OutboxRepository.ListForProjectAsync(projectId);

        // Oldest first by sequence_number: a consumer must never see the build
        // finish before it began.
        Assert.Collection(
            events,
            first => Assert.Equal(ConstructionEventTypes.ConstructionStarted, first.EventType),
            second => Assert.Equal(ConstructionEventTypes.ConstructionCompleted, second.EventType));

        using var document = JsonDocument.Parse(events[1].Envelope);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(3, payload.GetProperty("milestoneCount").GetInt32());
        // Both ends of the build ride along, so a consumer that came online
        // mid-build still learns when it started.
        Assert.True(
            payload.GetProperty("startedAt").GetDateTimeOffset()
            <= payload.GetProperty("completedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_refused_start_announces_nothing()
    {
        // No milestones, so the start is refused. The event is enqueued inside the
        // transition's transaction, so a refusal must leave the outbox empty — an
        // announced ConstructionStarted for a build that never started would move
        // the project into Construction in the Project Service on the strength of a
        // transition this service rejected.
        var projectId = _fixture.ProjectId("e04");
        await _fixture.PlantApprovedDesignAsync(projectId);

        var result = await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        Assert.Equal(ConstructionTransitionOutcome.NoMilestonesDefined, result.Outcome);
        Assert.Empty(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task A_refused_complete_announces_nothing_beyond_the_start()
    {
        var projectId = _fixture.ProjectId("e05");
        await _fixture.PlantApprovedDesignAsync(projectId);
        // One milestone left unfinished, so the complete is refused.
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Roof");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        var result = await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId);

        Assert.Equal(ConstructionTransitionOutcome.MilestonesIncomplete, result.Outcome);

        // Only the start is on the outbox; the refused complete added nothing.
        var announced = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
        Assert.Equal(ConstructionEventTypes.ConstructionStarted, announced.EventType);
    }

    [Fact]
    public async Task A_repeated_start_announces_nothing_a_second_time()
    {
        var projectId = _fixture.ProjectId("e06");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);
        var second = await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        Assert.Equal(ConstructionTransitionOutcome.AlreadyStarted, second.Outcome);

        // The duplicate-key rollback took the second event with it, so the topic
        // will not carry the same transition twice.
        Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task Pending_events_are_listed_oldest_first_for_the_dispatcher()
    {
        var projectId = _fixture.ProjectId("e07");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await CompleteEveryMilestoneAsync(projectId, "Foundation");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);
        await _fixture.ConstructionPhaseRepository.CompleteAsync(projectId);

        // The dispatcher's own query. Other runs' rows may share the table, so this
        // asserts the relative order of this project's two events rather than the
        // whole batch.
        var pending = await _fixture.OutboxRepository.ListPendingAsync(batchSize: 500);
        var mine = pending.Where(e => e.ProjectId == projectId).ToList();

        Assert.Equal(2, mine.Count);
        Assert.Equal(ConstructionEventTypes.ConstructionStarted, mine[0].EventType);
        Assert.Equal(ConstructionEventTypes.ConstructionCompleted, mine[1].EventType);
        Assert.True(mine[0].SequenceNumber < mine[1].SequenceNumber);
    }

    [Fact]
    public async Task A_delivered_event_stops_being_pending_and_a_failed_one_does_not()
    {
        var projectId = _fixture.ProjectId("e08");
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");
        await _fixture.ConstructionPhaseRepository.StartAsync(projectId);

        var announced = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));

        // A failed attempt is counted and explained, and the event stays pending so
        // the next pass tries again — that is what makes a broker outage a delay
        // rather than a lost event.
        await _fixture.OutboxRepository.MarkFailedAsync(announced.Id, "broker unreachable");

        var afterFailure = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
        Assert.False(afterFailure.IsPublished);
        Assert.Equal(1, afterFailure.AttemptCount);
        Assert.Equal("broker unreachable", afterFailure.LastError);
        Assert.Contains(
            await _fixture.OutboxRepository.ListPendingAsync(batchSize: 500),
            e => e.Id == announced.Id);

        // Delivery stamps the time, counts the attempt, and clears the stale error.
        await _fixture.OutboxRepository.MarkPublishedAsync(announced.Id, DateTime.UtcNow);

        var afterDelivery = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
        Assert.True(afterDelivery.IsPublished);
        Assert.Equal(2, afterDelivery.AttemptCount);
        Assert.Null(afterDelivery.LastError);
        Assert.DoesNotContain(
            await _fixture.OutboxRepository.ListPendingAsync(batchSize: 500),
            e => e.Id == announced.Id);
    }

    /// <summary>
    /// Plants the named milestones on the project and moves every one of them to
    /// <see cref="MilestoneStatus.Completed"/>.
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
