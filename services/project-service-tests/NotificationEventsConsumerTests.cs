using System.Text.Json;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="NotificationEventsConsumer.HandleAsync"/> — what one message off a topic does to the
/// stored notifications (US-26 AC-1).
/// </summary>
/// <remarks>
/// The Kafka plumbing around <c>HandleAsync</c> — subscribe, poll, commit, back off — is not
/// exercised here; what is pinned is the message-level decision and the exception contract
/// <c>ProcessAsync</c> maps to a commit. Which events count and what they say is
/// <c>NotificationMapperTests</c>; this is the wiring: find the project, store for whoever it
/// concerns, survive a redelivery.
/// </remarks>
public class NotificationEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid ArchitectId = Guid.Parse("c3d4e5f6-1111-4222-8333-444455556666");
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 20, 11, 0, 0, TimeSpan.Zero);

    private readonly StubProjectRepository _projects = new();
    private readonly FakeNotificationRepository _notifications = new();

    public NotificationEventsConsumerTests()
    {
        _projects.Project = new Project
        {
            Id = ProjectId,
            ClientId = ClientId,
            Name = "Beachfront villa",
            AssignedArchitectId = ArchitectId
        };
    }

    // --------------------------------------------------- AC-1: stored ----

    [Fact]
    public async Task An_approved_design_notifies_the_projects_client_and_architect()
    {
        await Consumer().HandleAsync(
            Envelope("DesignApproved", new { projectId = ProjectId, documentName = "Ground floor plan", versionNumber = 2 }),
            CancellationToken.None);

        Assert.Equal(
            new[] { ClientId, ArchitectId }.Order(),
            _notifications.Stored.Select(n => n.UserId).Order());
        Assert.All(_notifications.Stored, n => Assert.Equal("DesignApproved", n.EventType));
        Assert.All(_notifications.Stored, n => Assert.Equal(
            "Design \"Ground floor plan\" (version 2) was approved on \"Beachfront villa\".", n.Message));
    }

    [Fact]
    public async Task A_completed_milestone_notifies_the_projects_client_and_architect()
    {
        await Consumer().HandleAsync(
            Envelope("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" }),
            CancellationToken.None);

        Assert.Equal(2, _notifications.Stored.Count);
        Assert.All(_notifications.Stored, n => Assert.Equal("MilestoneCompleted", n.EventType));
    }

    [Fact]
    public async Task A_received_payment_notifies_the_projects_client_and_architect()
    {
        await Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId, amount = 5000m, invoiceStatus = "Paid" }),
            CancellationToken.None);

        Assert.Equal(2, _notifications.Stored.Count);
        Assert.All(_notifications.Stored, n => Assert.Equal("PaymentReceived", n.EventType));
    }

    [Fact]
    public async Task The_notification_is_stamped_with_the_event_it_came_from_not_with_now()
    {
        // A consumer that was down for an hour must not make the event look an hour newer.
        await Consumer().HandleAsync(
            Envelope("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" }),
            CancellationToken.None);

        Assert.All(_notifications.Stored, n =>
        {
            Assert.Equal(EventId, n.EventId);
            Assert.Equal(ProjectId, n.ProjectId);
            Assert.Equal(OccurredAt.UtcDateTime, n.OccurredAt);
            Assert.Null(n.ReadAt);
        });
    }

    [Fact]
    public async Task A_project_with_no_architect_yet_notifies_only_its_client()
    {
        _projects.Project!.AssignedArchitectId = null;

        await Consumer().HandleAsync(
            Envelope("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" }),
            CancellationToken.None);

        var notification = Assert.Single(_notifications.Stored);
        Assert.Equal(ClientId, notification.UserId);
    }

    [Fact]
    public async Task The_project_is_looked_up_by_the_id_the_event_names()
    {
        await Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId }),
            CancellationToken.None);

        Assert.Equal([ProjectId], _projects.GetByIdCalls);
    }

    [Fact]
    public async Task A_redelivered_event_does_not_notify_anyone_a_second_time()
    {
        // Kafka delivers at least once. The second pass builds new rows for the same event, and
        // the (event, person) rule is what has to stop them.
        var message = Envelope("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" });

        await Consumer().HandleAsync(message, CancellationToken.None);
        await Consumer().HandleAsync(message, CancellationToken.None);

        Assert.Equal(2, _notifications.Stored.Count);
    }

    [Fact]
    public async Task Two_different_events_each_notify()
    {
        await Consumer().HandleAsync(
            Envelope("MilestoneCompleted", new { projectId = ProjectId, name = "Foundation" }),
            CancellationToken.None);
        await Consumer().HandleAsync(
            Envelope("MilestoneCompleted", new { projectId = ProjectId, name = "Roof on" }, eventId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(4, _notifications.Stored.Count);
    }

    // ------------------------------------------------ what is left alone ----

    [Theory]
    [InlineData("DesignSubmitted")]
    [InlineData("DesignRevisionRequested")]
    [InlineData("InvoiceGenerated")]
    [InlineData("FinalPaymentSettled")]
    [InlineData("ConstructionStarted")]
    [InlineData("ConstructionCompleted")]
    [InlineData("SomethingElseEntirely")]
    public async Task An_event_type_that_is_not_a_notification_is_left_alone(string eventType)
    {
        await Consumer().HandleAsync(
            Envelope(eventType, new { projectId = ProjectId, documentName = "x", name = "x" }),
            CancellationToken.None);

        // Not even the project is looked up: it was not our event.
        Assert.Empty(_projects.GetByIdCalls);
        Assert.Equal(0, _notifications.InsertCalls);
    }

    [Fact]
    public async Task An_event_about_a_project_this_service_does_not_have_stores_nothing_and_is_not_an_error()
    {
        // Retrying cannot create the project, so this must be committed past, not thrown.
        _projects.Project = null;

        await Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId }),
            CancellationToken.None);

        Assert.Empty(_notifications.Stored);
        Assert.Equal(0, _notifications.InsertCalls);
    }

    // ------------------------------------------ the commit-or-retry contract ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json at all {")]
    [InlineData("{ \"eventType\": \"PaymentReceived\" ")]
    public async Task A_message_that_cannot_be_parsed_throws_JsonException(string body)
    {
        // ProcessAsync maps a JsonException to "commit past it".
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(body, CancellationToken.None));
        Assert.Empty(_notifications.Stored);
    }

    [Fact]
    public async Task A_notifiable_event_that_cannot_be_read_throws_JsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("DesignApproved", new { projectId = ProjectId }),
            CancellationToken.None));

        Assert.Empty(_notifications.Stored);
    }

    [Fact]
    public async Task A_storage_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        // ProcessAsync catches this, logs it, and does NOT commit — the message is re-read on the
        // next poll, and storing is deduplicated, so the retry is safe.
        _notifications.InsertThrows = new InvalidOperationException("project-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId }),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_project_lookup_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        _projects.GetThrows = new InvalidOperationException("project-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId }),
            CancellationToken.None));
    }

    // ----------------------------------------------------------- helpers ----

    private static string Envelope(string eventType, object payload, Guid? eventId = null) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId = eventId ?? EventId,
            occurredAt = OccurredAt,
            payload
        });

    private NotificationEventsConsumer Consumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IProjectRepository>(_ => _projects)
            .AddScoped<INotificationRepository>(_ => _notifications)
            .BuildServiceProvider();

        return new NotificationEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:29092",
                ConsumerGroupId = "project-service-tests"
            }),
            NullLogger<NotificationEventsConsumer>.Instance);
    }

    /// <summary>
    /// An <see cref="IProjectRepository"/> that answers the one lookup this consumer makes. The
    /// rest throw, so a change that starts using another member cannot pass unnoticed.
    /// </summary>
    private sealed class StubProjectRepository : IProjectRepository
    {
        public Project? Project { get; set; }

        public Exception? GetThrows { get; set; }

        public List<Guid> GetByIdCalls { get; } = [];

        public Task<Project?> GetByIdAsync(Guid id)
        {
            GetByIdCalls.Add(id);

            if (GetThrows is not null)
            {
                throw GetThrows;
            }

            return Task.FromResult(Project);
        }

        public Task InsertAsync(
            Project project,
            ProjectStatusChange creation,
            IReadOnlyList<OutboxEvent> outboxEvents) => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListAllAsync() => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            throw new NotSupportedException();

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents) => throw new NotSupportedException();

        public Task<bool> UpdatePaymentStatusAsync(
            Guid projectId,
            ProjectPaymentStatus paymentStatus,
            Guid sourceEventId,
            DateTime occurredAtUtc) => throw new NotSupportedException();

        public Task<bool> AssignArchitectAsync(
            Guid projectId,
            Guid architectId,
            ProjectStatusChange? transition,
            IReadOnlyList<OutboxEvent> outboxEvents,
            DateTime updatedAtUtc) => throw new NotSupportedException();

        public Task<bool> AssignProjectManagerAsync(
            Guid projectId,
            Guid projectManagerId,
            DateTime updatedAtUtc) => throw new NotSupportedException();
    }
}
