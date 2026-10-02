using System.Text.Json;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Reads <c>design-events</c>, <c>construction-events</c> and <c>payment-events</c> and stores a
/// notification for each person a <c>DesignApproved</c>, <c>MilestoneCompleted</c> or
/// <c>PaymentReceived</c> concerns (US-26 AC-1).
/// </summary>
/// <remarks>
/// Nothing is published: this only consumes events other stories already produce. Which events
/// count, and who they concern, is <see cref="NotificationMapper"/>'s decision; this class is the
/// plumbing around it — poll, load the project, store, commit.
/// <para>
/// <b>Its own consumer group.</b> <see cref="ConstructionEventsConsumer"/> and
/// <see cref="PaymentEventsConsumer"/> already read two of these topics for a different purpose,
/// under groups of their own. Joining either group would split the partitions between the two
/// readers, and each would see only some of the messages. A group of its own means this reader
/// sees every message on every topic, and the two purposes advance independently — a failure
/// storing a notification never holds up a project's status.
/// </para>
/// <para>
/// One reader for the three topics rather than three, because the group is the unit that has to
/// subscribe to the same topics on every member, and this one always does. It also avoids a
/// fourth, fifth and sixth copy of the poll loop.
/// </para>
/// <para>
/// Resilience follows the pattern the other consumers here established:
/// <list type="bullet">
/// <item>A message that cannot be parsed, or is one of ours but unreadable, is logged and its
/// offset committed past — it will never read, and wedging the partition on it would stop every
/// event behind it.</item>
/// <item>Any other event type is skipped without being read further.</item>
/// <item>A redelivery is absorbed by the table's unique (event, user) key, not treated as an
/// error.</item>
/// <item>A message whose handling fails — the database is unreachable — is <em>not</em>
/// committed, so the next poll re-reads it.</item>
/// <item>Nothing here reaches back to the publishing services.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class NotificationEventsConsumer : BackgroundService
{
    private static readonly string[] Topics = ["design-events", "construction-events", "payment-events"];

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<NotificationEventsConsumer> _logger;

    public NotificationEventsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<NotificationEventsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The group this consumer reads under: the configured id with <c>-notifications</c> appended.
    /// Distinct from every other consumer's, for the reason in the class remarks.
    /// </summary>
    private string GroupId => $"{_options.ConsumerGroupId}-notifications";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the request thread: BackgroundService runs ExecuteAsync inline during startup
        // until the first await, and the consume loop is a long-running one.
        await Task.Yield();

        var config = KafkaBrokerSettings.BuildConsumerConfig(_options, GroupId);

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topics);

        _logger.LogInformation(
            "Consuming {Topics} as group {GroupId} from {BootstrapServers}.",
            string.Join(", ", Topics), GroupId, _options.BootstrapServers);

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
            // Leave the group cleanly so a restart does not wait out the session timeout before
            // its partitions are reassigned.
            consumer.Close();
            _logger.LogInformation("Stopped consuming {Topics}.", string.Join(", ", Topics));
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
            // Shutting down mid-handling. Not committed — it re-delivers on the next start, and
            // the notification is deduplicated by event id, so a retry is safe.
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Skipping a message on {TopicPartitionOffset} that could not be read as a notifiable event.",
                result.TopicPartitionOffset);

            // Commit past it: a malformed message will not read on a retry, and holding the
            // partition on it would stop everything behind it.
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
    /// Processes one message body: an event we notify about is stored for each person it
    /// concerns, any other event type is a no-op.
    /// </summary>
    /// <remarks>
    /// The unit the tests drive directly. Its exception contract is what <see cref="ProcessAsync"/>
    /// maps to a commit decision: a <see cref="JsonException"/> is a message that will never read,
    /// so the caller commits past it; anything else is treated as transient, so the caller leaves
    /// the offset uncommitted and the message is retried.
    /// </remarks>
    public async Task HandleAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("The message body was empty.");
        }

        var envelope = JsonSerializer.Deserialize<IncomingEvent>(value, IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The message body deserialised to null.");

        var notifiable = NotificationMapper.Read(envelope);

        if (notifiable is null)
        {
            // DesignSubmitted, InvoiceGenerated, ConstructionStarted … — on the same topics, and
            // none is a notification. Committed by the caller.
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var project = await projects.GetByIdAsync(notifiable.ProjectId);

        if (project is null)
        {
            // Not one of ours. Retrying cannot create it, so it is committed past rather than
            // holding the partition.
            _logger.LogWarning(
                "{EventType} {EventId} is about project {ProjectId}, which this service does not have. "
                + "No notification stored.",
                notifiable.EventType, notifiable.EventId, notifiable.ProjectId);

            return;
        }

        var toStore = NotificationMapper.ToNotifications(notifiable, project);

        await notifications.InsertAsync(toStore, cancellationToken);

        _logger.LogInformation(
            "{EventType} {EventId} on project {ProjectId} notified {Count} people.",
            notifiable.EventType, notifiable.EventId, notifiable.ProjectId, toStore.Count);
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
