using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// Reads <c>project-events</c> and, for each <c>ProjectCreated</c>, records
/// which Client owns the project (US-13).
/// </summary>
/// <remarks>
/// US-13 opens the milestone and progress reads to the Client role, and a role
/// gate alone would let any signed-in Client read any project's build progress
/// by guessing an id. The owning Client is a fact the Project Service holds, and
/// this service may not query that database — one schema per service. So the
/// fact is replicated locally off an event the Project Service already
/// publishes; no new event type is introduced on either side.
/// <para>
/// The resilience rules are the ones <see cref="DesignEventsConsumer"/> already
/// set for this service, for the same reasons: a message that cannot be parsed
/// is logged and committed past, since it will never parse and holding the
/// partition on it would stop everything behind it; a message that parses but
/// whose handling fails is left uncommitted, so the next poll retries it — and
/// the ownership insert is idempotent, so a retry is safe. Nothing here reaches
/// back to the Project Service.
/// </para>
/// </remarks>
public sealed class ProjectEventsConsumer : BackgroundService
{
    private const string Topic = "project-events";
    private const string ProjectCreatedType = "ProjectCreated";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ProjectEventsConsumer> _logger;

    public ProjectEventsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ProjectEventsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The group this consumer reads under: the configured id with the topic
    /// appended.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> the bare <c>Kafka:ConsumerGroupId</c> that
    /// <see cref="DesignEventsConsumer"/> uses. Every member of a Kafka consumer
    /// group is expected to subscribe to the same topics; two members of one
    /// group subscribing to different topics makes each rebalance revoke the
    /// other's partitions, and the two consumers would take turns being
    /// assigned nothing. A group per topic keeps each one's offsets and
    /// assignment its own, and leaves the design consumer's committed offsets
    /// exactly where they are.
    /// </remarks>
    private string GroupId => $"{_options.ConsumerGroupId}-project-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the request thread: BackgroundService runs ExecuteAsync inline
        // during startup until the first await, and the consume loop is a
        // long-running one.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = GroupId,
            // Commit explicitly, only after a message is handled — so a crash
            // mid-handling re-delivers rather than skips.
            EnableAutoCommit = false,
            // A brand-new group reads the topic from the start, so the
            // ProjectCreated events published before this consumer first ran
            // are picked up rather than missed — which matters more here than
            // anywhere else in this service, because a project whose ownership
            // was never recorded is one its Client cannot see the progress of.
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topic);

        _logger.LogInformation(
            "Consuming {Topic} as group {GroupId} from {BootstrapServers}.",
            Topic, GroupId, _options.BootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;

                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed. Retrying after {Delay}.", RetryDelay);
                    await DelayAsync(stoppingToken);
                    continue;
                }

                await ProcessAsync(consumer, result, stoppingToken);
            }
        }
        finally
        {
            // Leave the group cleanly so a restart does not wait out the
            // session timeout before its partitions are reassigned.
            consumer.Close();
            _logger.LogInformation("Stopped consuming {Topic}.", Topic);
        }
    }

    private async Task ProcessAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        CancellationToken stoppingToken)
    {
        try
        {
            await HandleAsync(result.Message.Value, stoppingToken);
            consumer.Commit(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-handling. Not committed — it re-delivers on the
            // next start, and the ownership insert is idempotent.
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Skipping a message on {TopicPartitionOffset} that could not be parsed as a BuildNexus event.",
                result.TopicPartitionOffset);

            // Commit past it: a malformed message will not parse on a retry, and
            // holding the partition on it would stop everything behind it.
            consumer.Commit(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Handling the message on {TopicPartitionOffset} failed. Leaving the offset uncommitted to retry.",
                result.TopicPartitionOffset);

            await DelayAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Processes one message body: a <c>ProjectCreated</c> records the
    /// project's owning Client, any other event type on the topic is a no-op.
    /// </summary>
    /// <remarks>
    /// The unit the tests drive directly. Its exception contract is what
    /// <see cref="ProcessAsync"/> maps to a commit decision:
    /// a <see cref="JsonException"/> is a message that will never parse, so the
    /// caller commits past it; anything else is treated as transient, so the
    /// caller leaves the offset uncommitted and the message is retried.
    /// </remarks>
    public async Task HandleAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("The message body was empty.");
        }

        var envelope = JsonSerializer.Deserialize<IncomingEvent>(value, IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The message body deserialised to null.");

        if (!string.Equals(envelope.EventType, ProjectCreatedType, StringComparison.Ordinal))
        {
            // ProjectUpdated, ProjectApproved — on the same topic, and neither
            // tells this service anything it does not already have. A project
            // is never reassigned to a different Client, so ownership is
            // settled at creation. Committed by the caller.
            return;
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A ProjectCreated event carried no payload object.");
        }

        var payload = envelope.Payload.Deserialize<ProjectCreatedPayload>(IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The ProjectCreated payload deserialised to null.");

        if (payload.ProjectId == Guid.Empty || payload.ClientId == Guid.Empty)
        {
            throw new JsonException("The ProjectCreated payload was missing its projectId or clientId.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProjectOwnerRepository>();

        var recorded = await repository.RecordOwnerIfAbsentAsync(
            payload.ProjectId,
            payload.ClientId,
            cancellationToken);

        if (recorded)
        {
            _logger.LogInformation(
                "Recorded client {ClientId} as the owner of project {ProjectId} from ProjectCreated {EventId}.",
                payload.ClientId, payload.ProjectId, envelope.EventId);
        }
        else
        {
            _logger.LogInformation(
                "Project {ProjectId} already has a recorded owner; ProjectCreated {EventId} absorbed.",
                payload.ProjectId, envelope.EventId);
        }
    }

    private async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetryDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
