using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="ProjectEventsConsumer.HandleAsync"/> — what one message off
/// <c>project-events</c> does. The Kafka plumbing around it (subscribe, poll,
/// commit, back off) is the same code
/// <see cref="DesignEventsConsumerTests"/> describes; here the message-level
/// decisions are pinned.
/// </summary>
public class ProjectEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("3f1c9a54-7d2b-4c08-9f6e-1a2b3c4d5e6f");
    private static readonly Guid ClientId = Guid.Parse("a7b8c9d0-1e2f-4a3b-8c5d-6e7f8a9b0c1d");
    private static readonly Guid EventId = Guid.Parse("bb2f7a10-4c3d-4e5f-9a8b-7c6d5e4f3a2b");

    private readonly FakeProjectOwnerRepository _repository = new();

    [Fact]
    public async Task ProjectCreated_records_the_owning_client_from_the_payload()
    {
        await Consumer().HandleAsync(
            Envelope("ProjectCreated", EventId, new { projectId = ProjectId, clientId = ClientId }),
            CancellationToken.None);

        var call = Assert.Single(_repository.Calls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(ClientId, call.ClientId);
    }

    [Fact]
    public async Task The_wider_payload_the_Project_Service_publishes_is_read_without_complaint()
    {
        // project-service's own ProjectCreatedPayload carries the whole project
        // snapshot. This service reads two fields off it; the rest must be
        // ignored rather than refused, so a change to any other field on that
        // payload cannot break this consumer.
        await Consumer().HandleAsync(
            Envelope("ProjectCreated", EventId, new
            {
                projectId = ProjectId,
                clientId = ClientId,
                name = "Seaside Villa",
                location = "Galle",
                landSizePerches = 42.5m,
                budget = 95_000_000m,
                floors = 2,
                bedrooms = 4,
                bathrooms = 3,
                garageSpaces = 2,
                otherRequirements = (string?)null,
                status = "Pending",
                createdAt = DateTime.UtcNow
            }),
            CancellationToken.None);

        var call = Assert.Single(_repository.Calls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(ClientId, call.ClientId);
    }

    [Fact]
    public async Task A_project_whose_owner_is_already_recorded_is_not_an_error()
    {
        // Kafka delivers at least once, so a redelivered ProjectCreated has to
        // be absorbed rather than becoming a failure that blocks the partition.
        _repository.RecordResult = false;

        await Consumer().HandleAsync(
            Envelope("ProjectCreated", EventId, new { projectId = ProjectId, clientId = ClientId }),
            CancellationToken.None);

        Assert.Single(_repository.Calls);
    }

    [Theory]
    [InlineData("ProjectUpdated")]
    [InlineData("ProjectApproved")]
    [InlineData("SomethingElseEntirely")]
    public async Task An_event_that_is_not_ProjectCreated_is_left_alone(string eventType)
    {
        // A project is never reassigned to a different Client, so ownership is
        // settled at creation and the later lifecycle events tell this service
        // nothing it does not already have.
        await Consumer().HandleAsync(
            Envelope(eventType, EventId, new { projectId = ProjectId, clientId = ClientId }),
            CancellationToken.None);

        Assert.Empty(_repository.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json at all {")]
    [InlineData("{ \"eventType\": \"ProjectCreated\" ")]
    public async Task A_message_that_cannot_be_parsed_throws_JsonException(string body)
    {
        // ProcessAsync maps a JsonException to "commit past it": a malformed
        // message will not parse on a retry, and holding the partition on it
        // would stop everything behind it.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(body, CancellationToken.None));
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_ProjectCreated_with_no_payload_object_throws_JsonException()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            eventType = "ProjectCreated",
            eventId = EventId,
            occurredAt = DateTimeOffset.UtcNow,
            payload = (object?)null
        });

        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(envelope, CancellationToken.None));
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_ProjectCreated_missing_its_client_id_throws_JsonException()
    {
        // Recording Guid.Empty as a project's owner would be worse than
        // refusing the message: no Client has that id, so the project's real
        // owner would be locked out of its progress with no way to notice.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("ProjectCreated", EventId, new { projectId = ProjectId }),
            CancellationToken.None));

        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_ProjectCreated_missing_its_project_id_throws_JsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("ProjectCreated", EventId, new { clientId = ClientId }),
            CancellationToken.None));

        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_repository_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        // ProcessAsync catches this, logs it, and does NOT commit — the message
        // is re-read and retried on the next poll, and the consume loop stays
        // up. The ownership insert is idempotent, so the retry is safe.
        _repository.RecordThrows = new InvalidOperationException("construction-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            Envelope("ProjectCreated", EventId, new { projectId = ProjectId, clientId = ClientId }),
            CancellationToken.None));
    }

    private ProjectEventsConsumer Consumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IProjectOwnerRepository>(_ => _repository)
            .BuildServiceProvider();

        return new ProjectEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "unused:9092", ConsumerGroupId = "construction-service" }),
            NullLogger<ProjectEventsConsumer>.Instance);
    }

    private static string Envelope(string eventType, Guid eventId, object payload) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId,
            occurredAt = DateTimeOffset.UtcNow,
            payload
        });
}
