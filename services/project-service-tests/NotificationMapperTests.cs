using System.Text.Json;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="NotificationMapper"/> — which events become notifications, who is told, and what
/// they are told (US-26 AC-1). Given this event and this project, nothing else: no database, no
/// broker.
/// </summary>
public class NotificationMapperTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid ArchitectId = Guid.Parse("c3d4e5f6-1111-4222-8333-444455556666");
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 20, 11, 0, 0, TimeSpan.FromHours(5.5));

    // ------------------------------------------- which events count ----

    [Fact]
    public void The_three_events_the_story_names_are_the_ones_handled()
    {
        Assert.Equal(
            ["DesignApproved", "MilestoneCompleted", "PaymentReceived"],
            NotificationEventTypes.All.Order());
    }

    [Theory]
    [InlineData("DesignApproved")]
    [InlineData("MilestoneCompleted")]
    [InlineData("PaymentReceived")]
    public void A_named_event_is_handled(string eventType)
    {
        Assert.True(NotificationMapper.Handles(eventType));
    }

    [Theory]
    [InlineData("DesignSubmitted")]
    [InlineData("DesignRevisionRequested")]
    [InlineData("InvoiceGenerated")]
    [InlineData("FinalPaymentSettled")]
    [InlineData("ConstructionStarted")]
    [InlineData("ConstructionCompleted")]
    [InlineData("paymentreceived")]
    [InlineData("")]
    public void Any_other_event_is_left_alone(string eventType)
    {
        // Only the events US-26 names. Another event on the same topic is not this feature's to
        // announce, and a near miss in spelling is not a match.
        var envelope = Envelope(eventType, new { projectId = ProjectId, name = "x", documentName = "x" });

        Assert.False(NotificationMapper.Handles(eventType));
        Assert.Null(NotificationMapper.Read(envelope));
    }

    // -------------------------------------------- reading the event ----

    [Fact]
    public void A_design_approval_names_the_document_and_its_version()
    {
        var read = Read("DesignApproved", new { projectId = ProjectId, documentName = "Ground floor plan", versionNumber = 2 });

        Assert.Equal("Design \"Ground floor plan\" (version 2) was approved", read.WhatHappened);
        Assert.Equal(ProjectId, read.ProjectId);
    }

    [Fact]
    public void A_design_approval_with_no_usable_version_number_just_leaves_it_out()
    {
        var read = Read("DesignApproved", new { projectId = ProjectId, documentName = "Ground floor plan" });

        Assert.Equal("Design \"Ground floor plan\" was approved", read.WhatHappened);
    }

    [Fact]
    public void A_milestone_completion_names_the_milestone()
    {
        var read = Read("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" });

        Assert.Equal("Milestone \"Foundation\" was completed", read.WhatHappened);
        Assert.Equal(ProjectId, read.ProjectId);
    }

    [Fact]
    public void A_payment_says_it_was_received_and_leaves_the_amount_out()
    {
        // The Architect is a recipient, and the Payment Service refuses them every one of its
        // endpoints. A figure here would show them what they are not otherwise allowed to see.
        var read = Read(
            "PaymentReceived",
            new { projectId = ProjectId, amount = 123456.78m, paidBy = ClientId, invoiceStatus = "Paid" });

        Assert.Equal("A payment was received", read.WhatHappened);
        Assert.DoesNotContain("123", read.WhatHappened);
    }

    [Fact]
    public void The_event_id_and_when_it_happened_are_carried_through_as_UTC()
    {
        var read = Read("PaymentReceived", new { projectId = ProjectId });

        Assert.Equal(EventId, read.EventId);
        Assert.Equal(OccurredAt.UtcDateTime, read.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, read.OccurredAtUtc.Kind);
        Assert.Equal("PaymentReceived", read.EventType);
    }

    [Fact]
    public void Surrounding_whitespace_in_a_name_is_not_kept()
    {
        var read = Read("MilestoneCompleted", new { projectId = ProjectId, name = "  Foundation \t" });

        Assert.Equal("Milestone \"Foundation\" was completed", read.WhatHappened);
    }

    // ------------------------------------- events that cannot be read ----

    [Theory]
    [InlineData("DesignApproved")]
    [InlineData("MilestoneCompleted")]
    [InlineData("PaymentReceived")]
    public void A_handled_event_with_no_payload_object_throws_JsonException(string eventType)
    {
        // The consumer maps a JsonException to "commit past it": it will not read on a retry.
        var envelope = new IncomingEvent { EventType = eventType, EventId = EventId, OccurredAt = OccurredAt };

        Assert.Throws<JsonException>(() => NotificationMapper.Read(envelope));
    }

    [Theory]
    [InlineData("DesignApproved")]
    [InlineData("MilestoneCompleted")]
    [InlineData("PaymentReceived")]
    public void A_handled_event_with_no_project_throws_JsonException(string eventType)
    {
        var envelope = Envelope(eventType, new { documentName = "Plan", name = "Foundation" });

        Assert.Throws<JsonException>(() => NotificationMapper.Read(envelope));
    }

    [Theory]
    [InlineData("DesignApproved")]
    [InlineData("MilestoneCompleted")]
    [InlineData("PaymentReceived")]
    public void A_handled_event_with_no_event_id_throws_JsonException(string eventType)
    {
        // An empty id would collide with the next empty id on the (event, user) key, and the
        // second event would be dropped as a "redelivery" of the first.
        var envelope = Envelope(
            eventType, new { projectId = ProjectId, documentName = "Plan", name = "Foundation" }, eventId: Guid.Empty);

        Assert.Throws<JsonException>(() => NotificationMapper.Read(envelope));
    }

    [Theory]
    [InlineData("DesignApproved", "{\"projectId\":\"e059b652-129d-4a69-a4ab-e5e95ed4b543\"}")]
    [InlineData("DesignApproved", "{\"projectId\":\"e059b652-129d-4a69-a4ab-e5e95ed4b543\",\"documentName\":\"   \"}")]
    [InlineData("MilestoneCompleted", "{\"projectId\":\"e059b652-129d-4a69-a4ab-e5e95ed4b543\"}")]
    [InlineData("MilestoneCompleted", "{\"projectId\":\"e059b652-129d-4a69-a4ab-e5e95ed4b543\",\"name\":\"\"}")]
    public void A_named_thing_with_no_name_throws_JsonException(string eventType, string payload)
    {
        // A sentence about "" would read as a bug. Refused, not guessed at.
        var envelope = new IncomingEvent
        {
            EventType = eventType,
            EventId = EventId,
            OccurredAt = OccurredAt,
            Payload = JsonDocument.Parse(payload).RootElement
        };

        Assert.Throws<JsonException>(() => NotificationMapper.Read(envelope));
    }

    [Fact]
    public void A_project_id_that_is_not_an_id_throws_JsonException()
    {
        var envelope = Envelope("PaymentReceived", new { projectId = "not-a-guid" });

        Assert.Throws<JsonException>(() => NotificationMapper.Read(envelope));
    }

    // --------------------------------------------------- who is told ----

    [Fact]
    public void The_projects_client_and_its_architect_are_both_told()
    {
        var notifications = NotificationMapper.ToNotifications(Milestone(), ProjectOf(ClientId, ArchitectId));

        Assert.Equal(
            new[] { ClientId, ArchitectId }.Order(),
            notifications.Select(n => n.UserId).Order());
    }

    [Fact]
    public void A_project_with_no_architect_yet_tells_only_its_client()
    {
        var notification = Assert.Single(NotificationMapper.ToNotifications(Milestone(), ProjectOf(ClientId, null)));

        Assert.Equal(ClientId, notification.UserId);
    }

    [Fact]
    public void Nobody_is_told_twice_if_one_person_holds_both_seats()
    {
        var notification = Assert.Single(NotificationMapper.ToNotifications(Milestone(), ProjectOf(ClientId, ClientId)));

        Assert.Equal(ClientId, notification.UserId);
    }

    [Fact]
    public void The_project_manager_is_not_a_recipient()
    {
        var project = ProjectOf(ClientId, ArchitectId);
        project.AssignedProjectManagerId = Guid.NewGuid();

        var notifications = NotificationMapper.ToNotifications(Milestone(), project);

        Assert.DoesNotContain(notifications, n => n.UserId == project.AssignedProjectManagerId);
    }

    [Theory]
    [InlineData("DesignApproved")]
    [InlineData("MilestoneCompleted")]
    [InlineData("PaymentReceived")]
    public void Every_event_tells_the_client_and_the_architect_alike(string eventType)
    {
        var read = Read(eventType, new { projectId = ProjectId, documentName = "Plan", name = "Foundation" });

        var notifications = NotificationMapper.ToNotifications(read, ProjectOf(ClientId, ArchitectId));

        Assert.Equal(2, notifications.Count);
    }

    // ------------------------------------------------- what they get ----

    [Fact]
    public void The_message_is_the_sentence_followed_by_the_project_it_is_about()
    {
        var notification = Assert.Single(NotificationMapper.ToNotifications(Milestone(), ProjectOf(ClientId, null)));

        Assert.Equal("Milestone \"Foundation\" was completed on \"Beachfront villa\".", notification.Message);
    }

    [Fact]
    public void Each_notification_carries_the_events_identity_the_project_and_the_time()
    {
        var notification = Assert.Single(NotificationMapper.ToNotifications(Milestone(), ProjectOf(ClientId, null)));

        Assert.Equal(EventId, notification.EventId);
        Assert.Equal(ProjectId, notification.ProjectId);
        Assert.Equal("MilestoneCompleted", notification.EventType);
        Assert.Equal(OccurredAt.UtcDateTime, notification.OccurredAt);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public void Two_people_get_separate_rows_that_share_the_events_id()
    {
        // Different row ids, same event id: the (event, user) key is what recognises a redelivery.
        var notifications = NotificationMapper.ToNotifications(Milestone(), ProjectOf(ClientId, ArchitectId));

        Assert.NotEqual(notifications[0].Id, notifications[1].Id);
        Assert.All(notifications, n => Assert.Equal(EventId, n.EventId));
    }

    [Fact]
    public void A_message_longer_than_its_column_is_cut_to_fit_rather_than_refused()
    {
        // A name the database refuses would be retried forever and hold the partition.
        var read = Read("MilestoneCompleted", new { projectId = ProjectId, name = new string('m', 800) });

        var notification = Assert.Single(NotificationMapper.ToNotifications(read, ProjectOf(ClientId, null)));

        Assert.Equal(NotificationMapper.MaxMessageLength, notification.Message.Length);
        Assert.EndsWith("…", notification.Message);
    }

    [Fact]
    public void A_message_that_fits_is_left_exactly_as_written()
    {
        var read = Read("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" });

        var notification = Assert.Single(NotificationMapper.ToNotifications(read, ProjectOf(ClientId, null)));

        Assert.DoesNotContain("…", notification.Message);
    }

    // ----------------------------------------------------------- helpers ----

    private static NotifiableEvent Milestone() =>
        Read("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" });

    private static NotifiableEvent Read(string eventType, object payload) =>
        NotificationMapper.Read(Envelope(eventType, payload))
            ?? throw new InvalidOperationException($"{eventType} was not handled.");

    private static IncomingEvent Envelope(string eventType, object payload, Guid? eventId = null) =>
        JsonSerializer.Deserialize<IncomingEvent>(
            JsonSerializer.Serialize(new
            {
                eventType,
                eventId = eventId ?? EventId,
                occurredAt = OccurredAt,
                payload
            }),
            IncomingEvent.SerializerOptions)!;

    private static Project ProjectOf(Guid clientId, Guid? architectId) =>
        new()
        {
            Id = ProjectId,
            ClientId = clientId,
            Name = "Beachfront villa",
            AssignedArchitectId = architectId
        };
}
