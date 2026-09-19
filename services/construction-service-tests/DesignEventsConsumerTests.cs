using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="DesignEventsConsumer.HandleAsync"/> — what one message off
/// <c>design-events</c> does. The Kafka plumbing around it (subscribe, poll,
/// commit, back off) is exercised end to end against a real broker in the
/// commit that added the consumer; here the message-level decisions are pinned.
/// </summary>
public class DesignEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid DocumentId = Guid.Parse("0143720f-166c-4c34-b14d-b9980bd55222");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly DateTimeOffset ApprovedAt = new(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeMilestoneSetupRepository _repository = new();

    [Fact]
    public async Task DesignApproved_creates_a_placeholder_from_the_envelope_and_payload()
    {
        await Consumer().HandleAsync(
            Envelope("DesignApproved", EventId, new { projectId = ProjectId, documentId = DocumentId, approvedAt = ApprovedAt }),
            CancellationToken.None);

        var call = Assert.Single(_repository.Calls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(DocumentId, call.SourceDocumentId);
        Assert.Equal(EventId, call.SourceEventId);
        Assert.Equal(ApprovedAt.UtcDateTime, call.ApprovedAtUtc);
    }

    [Fact]
    public async Task A_project_that_already_has_a_placeholder_is_not_an_error()
    {
        _repository.CreateResult = false;

        await Consumer().HandleAsync(
            Envelope("DesignApproved", EventId, new { projectId = ProjectId, documentId = DocumentId, approvedAt = ApprovedAt }),
            CancellationToken.None);

        Assert.Single(_repository.Calls);
    }

    [Theory]
    [InlineData("DesignSubmitted")]
    [InlineData("DesignRevisionRequested")]
    [InlineData("SomethingElseEntirely")]
    public async Task An_event_that_is_not_DesignApproved_is_left_alone(string eventType)
    {
        await Consumer().HandleAsync(
            Envelope(eventType, EventId, new { projectId = ProjectId, documentId = DocumentId, approvedAt = ApprovedAt }),
            CancellationToken.None);

        Assert.Empty(_repository.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json at all {")]
    [InlineData("{ \"eventType\": \"DesignApproved\" ")]
    public async Task A_message_that_cannot_be_parsed_throws_JsonException(string body)
    {
        // ProcessAsync maps a JsonException to "commit past it": a malformed
        // message will not parse on a retry, and holding the partition on it
        // would stop everything behind it.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(body, CancellationToken.None));
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_DesignApproved_with_no_payload_object_throws_JsonException()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            eventType = "DesignApproved",
            eventId = EventId,
            occurredAt = ApprovedAt,
            payload = (object?)null
        });

        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(envelope, CancellationToken.None));
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_DesignApproved_missing_its_project_id_throws_JsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("DesignApproved", EventId, new { documentId = DocumentId, approvedAt = ApprovedAt }),
            CancellationToken.None));

        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_repository_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        // ProcessAsync catches this, logs it, and does NOT commit — the message
        // is re-read and retried on the next poll, and the consume loop stays
        // up. The placeholder insert is idempotent, so the retry is safe.
        _repository.CreateThrows = new InvalidOperationException("construction-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            Envelope("DesignApproved", EventId, new { projectId = ProjectId, documentId = DocumentId, approvedAt = ApprovedAt }),
            CancellationToken.None));
    }

    private DesignEventsConsumer Consumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IMilestoneSetupRepository>(_ => _repository)
            .BuildServiceProvider();

        return new DesignEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "unused:9092", ConsumerGroupId = "construction-service" }),
            NullLogger<DesignEventsConsumer>.Instance);
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
