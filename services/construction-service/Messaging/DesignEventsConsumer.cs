using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// Reads <c>design-events</c> and, for each <c>DesignApproved</c>, makes sure the
/// project has a milestone-setup placeholder (US-23).
/// </summary>
/// <remarks>
/// The first consumer in BuildNexus, so the resilience the story asks for is
/// spelled out here:
/// <list type="bullet">
/// <item>A message that cannot be parsed is logged and its offset committed
/// past — it will never parse, and wedging the partition on it would stop every
/// event behind it.</item>
/// <item>A message that parses but whose handling fails — the database is
/// unreachable, say — is <em>not</em> committed. The next poll re-reads it from
/// the uncommitted offset and tries again; the placeholder insert is idempotent,
/// so a retry is safe.</item>
/// <item>Nothing here reaches back to the Design Service. A consume failure is
/// this service's problem alone — the publisher already committed its event to
/// its own outbox and moved on.</item>
/// </list>
/// </remarks>
public sealed class DesignEventsConsumer : BackgroundService
{
    private const string Topic = "design-events";
    private const string DesignApprovedType = "DesignApproved";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<DesignEventsConsumer> _logger;

    public DesignEventsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<DesignEventsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the request thread: BackgroundService runs ExecuteAsync inline
        // during startup until the first await, and the consume loop is a
        // long-running one.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            // Commit explicitly, only after a message is handled — so a crash
            // mid-handling re-delivers rather than skips.
            EnableAutoCommit = false,
            // A brand-new group reads the topic from the start, so events
            // published before this service first ran are not missed.
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topic);

        _logger.LogInformation(
            "Consuming {Topic} as group {GroupId} from {BootstrapServers}.",
            Topic, _options.ConsumerGroupId, _options.BootstrapServers);

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
            // next start, and the placeholder insert is idempotent.
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
    /// Processes one message body: a <c>DesignApproved</c> becomes a
    /// milestone-setup placeholder, any other event type on the topic is a no-op.
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

        if (!string.Equals(envelope.EventType, DesignApprovedType, StringComparison.Ordinal))
        {
            // DesignSubmitted, DesignRevisionRequested — on the same topic, not
            // this service's concern. Committed by the caller.
            return;
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A DesignApproved event carried no payload object.");
        }

        var payload = envelope.Payload.Deserialize<DesignApprovedPayload>(IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The DesignApproved payload deserialised to null.");

        if (payload.ProjectId == Guid.Empty || payload.DocumentId == Guid.Empty)
        {
            throw new JsonException("The DesignApproved payload was missing its projectId or documentId.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMilestoneSetupRepository>();

        var created = await repository.CreatePlaceholderIfAbsentAsync(
            payload.ProjectId,
            payload.DocumentId,
            envelope.EventId,
            payload.ApprovedAt.UtcDateTime,
            cancellationToken);

        if (created)
        {
            _logger.LogInformation(
                "Created a milestone-setup placeholder for project {ProjectId} from DesignApproved {EventId}.",
                payload.ProjectId, envelope.EventId);
        }
        else
        {
            _logger.LogInformation(
                "Project {ProjectId} already has a milestone-setup placeholder; DesignApproved {EventId} absorbed.",
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
