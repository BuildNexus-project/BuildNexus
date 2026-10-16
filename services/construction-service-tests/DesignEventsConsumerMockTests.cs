using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// What <see cref="DesignEventsConsumer.HandleAsync"/> asks of the milestone-setup
/// repository for each message body, checked with a Moq mock (US-14, AC-1).
/// </summary>
/// <remarks>
/// The placeholder this consumer creates is what opens the milestone gate: no
/// placeholder, no milestones. So only a valid <c>DesignApproved</c> may reach the
/// repository, and every other message must leave it untouched.
/// </remarks>
public class DesignEventsConsumerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid DocumentId = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset ApprovedAt = new(2026, 8, 31, 14, 5, 0, TimeSpan.Zero);

    private readonly Mock<IMilestoneSetupRepository> _setups = new();

    public static TheoryData<string> PayloadsMissingAnId => new()
    {
        "{}",
        $"{{\"projectId\":\"{ProjectId}\"}}",
        $"{{\"documentId\":\"{DocumentId}\"}}",
        $"{{\"projectId\":\"{Guid.Empty}\",\"documentId\":\"{DocumentId}\"}}",
        $"{{\"projectId\":\"{ProjectId}\",\"documentId\":\"{Guid.Empty}\"}}"
    };

    [Fact]
    public async Task A_DesignApproved_creates_the_placeholder_once_with_the_ids_and_time_from_the_message()
    {
        StubCreate(created: true);

        await HandleAsync(Envelope("DesignApproved", ApprovedPayload()));

        _setups.Verify(
            s => s.CreatePlaceholderIfAbsentAsync(
                ProjectId, DocumentId, EventId, ApprovedAt.UtcDateTime, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_DesignApproved_for_a_project_that_already_has_a_placeholder_is_absorbed_not_an_error()
    {
        // Duplicate event: the repository reports nothing new was created.
        StubCreate(created: false);

        var failure = await Record.ExceptionAsync(() => HandleAsync(Envelope("DesignApproved", ApprovedPayload())));

        Assert.Null(failure);
        _setups.Verify(
            s => s.CreatePlaceholderIfAbsentAsync(
                ProjectId, DocumentId, EventId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_asks_the_repository_twice_with_the_same_event_id()
    {
        StubCreate(created: true);
        var message = Envelope("DesignApproved", ApprovedPayload());

        await HandleAsync(message);
        await HandleAsync(message);

        _setups.Verify(
            s => s.CreatePlaceholderIfAbsentAsync(
                ProjectId, DocumentId, EventId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData("DesignSubmitted")]
    [InlineData("DesignRevisionRequested")]
    [InlineData("ProjectCreated")]
    [InlineData("designapproved")]
    public async Task Any_other_event_type_is_ignored_and_the_repository_is_never_called(string eventType)
    {
        // The last one is the right name in the wrong case: the comparison is exact.
        await HandleAsync(Envelope(eventType, ApprovedPayload()));

        _setups.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(PayloadsMissingAnId))]
    public async Task A_DesignApproved_missing_or_zeroing_an_id_is_a_json_failure_and_creates_nothing(string payload)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(Envelope("DesignApproved", payload)));

        _setups.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task A_body_that_will_never_parse_is_a_json_failure_and_creates_nothing(string body)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(body));

        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_DesignApproved_whose_payload_is_not_an_object_is_a_json_failure_and_creates_nothing()
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(Envelope("DesignApproved", "\"just text\"")));

        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_placeholder_write_surfaces_as_something_other_than_a_json_failure()
    {
        // So the caller leaves the offset uncommitted and the message is retried.
        _setups
            .Setup(s => s.CreatePlaceholderIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("construction-db is unreachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandleAsync(Envelope("DesignApproved", ApprovedPayload())));
    }

    private void StubCreate(bool created) =>
        _setups
            .Setup(s => s.CreatePlaceholderIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

    private static string ApprovedPayload() =>
        $"{{\"projectId\":\"{ProjectId}\",\"documentId\":\"{DocumentId}\",\"approvedAt\":\"{ApprovedAt:O}\"}}";

    private static string Envelope(string eventType, string payloadJson) =>
        $"{{\"eventType\":\"{eventType}\",\"eventId\":\"{EventId}\","
        + $"\"occurredAt\":\"{ApprovedAt:O}\",\"payload\":{payloadJson}}}";

    private Task HandleAsync(string? body)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_setups.Object);

        var consumer = new DesignEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<DesignEventsConsumer>.Instance);

        return consumer.HandleAsync(body, CancellationToken.None);
    }
}
