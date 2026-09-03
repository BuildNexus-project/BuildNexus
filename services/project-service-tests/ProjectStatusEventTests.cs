using System.Text.Json;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The shape of what a status change puts onto <c>project-events</c> (US-22).
/// </summary>
/// <remarks>
/// The same reasoning as <see cref="ProjectCreatedEventTests"/>: this is a
/// contract with four other services and nothing checks it at compile time, so
/// a renamed property or a dropped field fails at runtime in somebody else's
/// service rather than here. No broker is needed — the events are minted and
/// serialised in-process.
/// </remarks>
public class ProjectStatusEventTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid ArchitectId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTime ChangedAt = new(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc);

    // ------------------------------------------------- what a move raises ----

    [Fact]
    public void An_ordinary_move_raises_only_ProjectUpdated()
    {
        var raised = ProjectEvents.ForStatusChange(
            ProjectFor(), ChangeTo(ProjectStatus.Pending, ProjectStatus.Designing));

        var only = Assert.Single(raised);
        Assert.Equal(ProjectEventTypes.ProjectUpdated, only.EventType);
    }

    [Fact]
    public void The_move_onto_DesignApproved_raises_the_update_and_then_the_approval()
    {
        // Both, and in that order. A consumer reading the two would otherwise
        // see the project approved before it was told the project had moved —
        // a state it never actually passed through.
        var raised = ProjectEvents.ForStatusChange(ProjectFor(), ApprovalChange());

        Assert.Equal(
            [ProjectEventTypes.ProjectUpdated, ProjectEventTypes.ProjectApproved],
            raised.Select(e => e.EventType));
    }

    [Theory]
    [InlineData(ProjectStatus.DesignApproved, ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Construction, ProjectStatus.Completed)]
    public void No_later_move_raises_a_second_approval(ProjectStatus from, ProjectStatus to)
    {
        // Approval happens once. A project reaching Construction has already
        // been approved, and announcing it again would have Payment raise a
        // second first invoice.
        var raised = ProjectEvents.ForStatusChange(ProjectFor(), ChangeTo(from, to));

        Assert.DoesNotContain(raised, e => e.EventType == ProjectEventTypes.ProjectApproved);
    }

    [Fact]
    public void Every_transition_the_lifecycle_allows_raises_an_update()
    {
        // Written over the transition table rather than over today's four moves,
        // so a stage added later is held to the same rule: whatever a project
        // may do, somebody is told it did it.
        foreach (var from in Enum.GetValues<ProjectStatus>())
        {
            foreach (var to in ProjectStatusTransitions.NextFrom(from))
            {
                var raised = ProjectEvents.ForStatusChange(ProjectFor(), ChangeTo(from, to));

                Assert.Contains(raised, e => e.EventType == ProjectEventTypes.ProjectUpdated);
            }
        }
    }

    // --------------------------------------------------- the outbox row ----

    [Fact]
    public void Each_event_is_keyed_by_its_project_so_its_order_survives_the_topic()
    {
        // The Kafka message key. Every event about one project lands on the same
        // partition, which is the only thing that keeps them in order.
        var raised = ProjectEvents.ForStatusChange(ProjectFor(), ApprovalChange());

        Assert.All(raised, e => Assert.Equal(ProjectId, e.ProjectId));
    }

    [Fact]
    public void The_row_and_its_envelope_carry_the_same_id()
    {
        // What lets a message on the topic be traced back to the row that
        // produced it.
        var raised = ProjectEvents.ForStatusChange(ProjectFor(), ApprovalChange());

        foreach (var outboxEvent in raised)
        {
            var envelopeId = Read(outboxEvent).RootElement.GetProperty("eventId").GetString();

            Assert.Equal(outboxEvent.Id.ToString(), envelopeId);
        }
    }

    [Fact]
    public void Two_events_from_one_change_do_not_share_an_id()
    {
        // A consumer deduplicating on eventId would otherwise drop the approval
        // as a repeat of the update.
        var raised = ProjectEvents.ForStatusChange(ProjectFor(), ApprovalChange());

        Assert.Equal(2, raised.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void A_new_event_is_pending_and_has_not_been_tried()
    {
        var raised = Assert.Single(ProjectEvents.ForStatusChange(
            ProjectFor(), ChangeTo(ProjectStatus.Pending, ProjectStatus.Designing)));

        Assert.False(raised.IsPublished);
        Assert.Null(raised.PublishedAt);
        Assert.Equal(0, raised.AttemptCount);
        Assert.Null(raised.LastError);
    }

    // ------------------------------------------------------- the envelope ----

    [Theory]
    [InlineData(ProjectEventTypes.ProjectUpdated)]
    [InlineData(ProjectEventTypes.ProjectApproved)]
    public void The_envelope_carries_exactly_the_four_agreed_properties(string eventType)
    {
        var properties = Read(RaisedOf(eventType)).RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(["eventType", "eventId", "occurredAt", "payload"], properties);
    }

    [Theory]
    [InlineData(ProjectEventTypes.ProjectUpdated)]
    [InlineData(ProjectEventTypes.ProjectApproved)]
    public void The_envelope_names_the_event(string eventType)
    {
        Assert.Equal(eventType, Read(RaisedOf(eventType)).RootElement.GetProperty("eventType").GetString());
    }

    [Theory]
    [InlineData(ProjectEventTypes.ProjectUpdated)]
    [InlineData(ProjectEventTypes.ProjectApproved)]
    public void The_time_it_happened_is_the_moment_of_the_change_in_iso_8601(string eventType)
    {
        // The change's own time, not the time it was minted or later sent.
        Assert.Equal(
            "2026-08-31T14:05:00+00:00",
            Read(RaisedOf(eventType)).RootElement.GetProperty("occurredAt").GetString());
    }

    // ------------------------------------------------------- ProjectUpdated ----

    [Fact]
    public void The_update_payload_carries_the_move_and_who_made_it()
    {
        var payload = Payload(RaisedOf(ProjectEventTypes.ProjectUpdated));

        Assert.Equal(ProjectId.ToString(), payload.GetProperty("projectId").GetString());
        Assert.Equal(ClientId.ToString(), payload.GetProperty("clientId").GetString());
        Assert.Equal("Beachfront villa", payload.GetProperty("name").GetString());
        Assert.Equal("Designing", payload.GetProperty("previousStatus").GetString());
        Assert.Equal("DesignApproved", payload.GetProperty("status").GetString());
        Assert.Equal(ArchitectId.ToString(), payload.GetProperty("changedByUserId").GetString());
        Assert.Equal("Architect", payload.GetProperty("changedByRole").GetString());
    }

    [Fact]
    public void The_update_payload_leaves_the_requirements_off()
    {
        // The second AC bullet: the project id and the minimal context, not the
        // whole project. The requirements have not changed, and repeating them
        // on every transition would put the description on the topic four more
        // times for nothing.
        var payload = Payload(RaisedOf(ProjectEventTypes.ProjectUpdated));

        foreach (var absent in (string[])["landSizePerches", "floors", "bedrooms", "bathrooms", "garageSpaces"])
        {
            Assert.False(payload.TryGetProperty(absent, out _), $"'{absent}' should not be on a move.");
        }
    }

    [Fact]
    public void The_update_payload_carries_statuses_as_their_names()
    {
        // Not the enum's numbers: a consumer must not be tied to our ordering.
        var payload = Payload(RaisedOf(ProjectEventTypes.ProjectUpdated));

        Assert.Equal(JsonValueKind.String, payload.GetProperty("previousStatus").ValueKind);
        Assert.Equal(JsonValueKind.String, payload.GetProperty("status").ValueKind);
    }

    // ------------------------------------------------------ ProjectApproved ----

    [Fact]
    public void The_approval_payload_carries_who_to_bill_and_what_for()
    {
        // What the Payment Service needs to raise its first invoice, and the
        // Construction Service to schedule the build, without calling back here.
        var payload = Payload(RaisedOf(ProjectEventTypes.ProjectApproved));

        Assert.Equal(ProjectId.ToString(), payload.GetProperty("projectId").GetString());
        Assert.Equal(ClientId.ToString(), payload.GetProperty("clientId").GetString());
        Assert.Equal("Beachfront villa", payload.GetProperty("name").GetString());
        Assert.Equal(18_500_000m, payload.GetProperty("budget").GetDecimal());
        Assert.Equal(ArchitectId.ToString(), payload.GetProperty("approvedByUserId").GetString());
        Assert.Equal("Architect", payload.GetProperty("approvedByRole").GetString());
    }

    [Fact]
    public void The_approval_payload_says_nothing_about_status()
    {
        // The event says one thing and says it in its type. A consumer that
        // wants the lifecycle is reading ProjectUpdated.
        Assert.False(Payload(RaisedOf(ProjectEventTypes.ProjectApproved)).TryGetProperty("status", out _));
    }

    // -------------------------------------------------------- timestamps ----

    [Theory]
    [InlineData(ProjectEventTypes.ProjectUpdated, "changedAt")]
    [InlineData(ProjectEventTypes.ProjectApproved, "approvedAt")]
    public void Payload_times_carry_their_offset_even_when_the_database_dropped_it(
        string eventType,
        string property)
    {
        // The regression this guards: MySQL's DATETIME carries no offset, so a
        // value read back out of it arrives as DateTimeKind.Unspecified. Left
        // alone, DateTimeOffset would stamp it with the *server's* local zone,
        // and the event would claim a time that never happened anywhere but on
        // a machine set to UTC.
        var readBackFromMySql = DateTime.SpecifyKind(ChangedAt, DateTimeKind.Unspecified);

        var raised = ProjectEvents
            .ForStatusChange(ProjectFor(), ApprovalChange(readBackFromMySql))
            .Single(e => e.EventType == eventType);

        Assert.Equal("2026-08-31T14:05:00+00:00", Payload(raised).GetProperty(property).GetString());
        Assert.Equal(
            "2026-08-31T14:05:00+00:00",
            Read(raised).RootElement.GetProperty("occurredAt").GetString());
    }

    // ------------------------------------------------------------ helpers ----

    private static OutboxEvent RaisedOf(string eventType) =>
        ProjectEvents.ForStatusChange(ProjectFor(), ApprovalChange()).Single(e => e.EventType == eventType);

    private static JsonDocument Read(OutboxEvent outboxEvent) => JsonDocument.Parse(outboxEvent.Envelope);

    private static JsonElement Payload(OutboxEvent outboxEvent) =>
        Read(outboxEvent).RootElement.GetProperty("payload");

    /// <summary>The move that approves a design, which raises both events.</summary>
    private static ProjectStatusChange ApprovalChange(DateTime? changedAt = null) =>
        ChangeTo(ProjectStatus.Designing, ProjectStatus.DesignApproved, changedAt);

    private static ProjectStatusChange ChangeTo(
        ProjectStatus from,
        ProjectStatus to,
        DateTime? changedAt = null) => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = ArchitectId,
            ChangedByRole = "Architect",
            ChangedAt = changedAt ?? ChangedAt
        };

    private static Project ProjectFor() => new()
    {
        Id = ProjectId,
        ClientId = ClientId,
        Name = "Beachfront villa",
        Location = "Galle",
        LandSizePerches = 25.5m,
        Budget = 18_500_000m,
        Floors = 2,
        Bedrooms = 4,
        Bathrooms = 3,
        GarageSpaces = 2,
        OtherRequirements = "Solar hot water",
        Status = ProjectStatus.Designing,
        CreatedAt = new DateTime(2026, 8, 30, 9, 15, 0, DateTimeKind.Utc),
        UpdatedAt = ChangedAt
    };
}
