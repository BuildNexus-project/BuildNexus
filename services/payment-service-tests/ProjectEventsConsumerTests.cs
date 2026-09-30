using System.Text.Json;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="ProjectEventsConsumer.HandleAsync"/> does with one message
/// body (US-15).
/// </summary>
/// <remarks>
/// Drives the handler directly rather than through a broker: what is under test
/// is the decision made per message, and a real Kafka would only make the run
/// depend on how long it took to answer. The exception contract matters as much
/// as the happy path — a <see cref="JsonException"/> is what makes the caller
/// commit past a message that will never parse, and anything else is what makes
/// it leave the offset uncommitted to retry.
/// </remarks>
public class ProjectEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly FakeProjectOwnerRepository _owners = new();

    [Fact]
    public async Task A_ProjectCreated_records_the_projects_owning_client()
    {
        await HandleAsync(Envelope("ProjectCreated", new { projectId = ProjectId, clientId = ClientId }));

        Assert.Equal(ClientId, _owners.OwnerOf(ProjectId));
    }

    [Fact]
    public async Task A_redelivered_ProjectCreated_is_absorbed_rather_than_failing()
    {
        // Kafka delivers at least once. A second copy must be a no-op, or the
        // consumer would wedge its partition on a message it can never get past.
        var message = Envelope("ProjectCreated", new { projectId = ProjectId, clientId = ClientId });

        await HandleAsync(message);
        await HandleAsync(message);

        Assert.Equal(ClientId, _owners.OwnerOf(ProjectId));
    }

    [Fact]
    public async Task Another_event_on_the_topic_is_ignored()
    {
        // project-events carries more than ProjectCreated. None of the rest tells
        // this service anything it does not already have — a project is never
        // reassigned to a different Client.
        await HandleAsync(Envelope("ProjectUpdated", new { projectId = ProjectId, clientId = ClientId }));

        Assert.Null(_owners.OwnerOf(ProjectId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task A_message_that_will_never_parse_is_reported_as_a_json_failure(string body)
    {
        // Which is what makes the caller commit past it instead of retrying it
        // forever and blocking everything behind it on the partition.
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(body));
    }

    [Theory]
    [InlineData("{\"projectId\":\"aaaaaaaa-0000-4000-8000-000000000001\"}")]
    [InlineData("{\"clientId\":\"bbbbbbbb-0000-4000-8000-000000000001\"}")]
    [InlineData("{}")]
    public async Task A_ProjectCreated_missing_either_id_is_reported_as_a_json_failure(string payload)
    {
        // An ownership row needs both halves. Recording a half-empty pair would
        // either gate on Guid.Empty or silently deny the real owner.
        var message = $"{{\"eventType\":\"ProjectCreated\",\"eventId\":\"{Guid.NewGuid()}\","
                      + $"\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":{payload}}}";

        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(message));
        Assert.Null(_owners.OwnerOf(ProjectId));
    }

    [Fact]
    public async Task A_ProjectCreated_with_no_payload_object_is_reported_as_a_json_failure()
    {
        var message = $"{{\"eventType\":\"ProjectCreated\",\"eventId\":\"{Guid.NewGuid()}\","
                      + $"\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":null}}";

        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(message));
    }

    [Fact]
    public async Task A_failing_write_surfaces_as_something_other_than_a_json_failure()
    {
        // The distinction the caller's commit decision turns on: a database that
        // is down is transient, so the offset must be left uncommitted and the
        // message retried — not committed past like unparseable input.
        _owners.NextWriteThrows = new InvalidOperationException("payment-db is unreachable.");

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => HandleAsync(Envelope("ProjectCreated", new { projectId = ProjectId, clientId = ClientId })));

        Assert.IsNotType<JsonException>(failure);
    }

    private static string Envelope(string eventType, object payload) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            payload
        });

    private Task HandleAsync(string? body)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProjectOwnerRepository>(_owners);

        var consumer = new ProjectEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<ProjectEventsConsumer>.Instance);

        return consumer.HandleAsync(body, CancellationToken.None);
    }
}
