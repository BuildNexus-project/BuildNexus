using System.Text.Json;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Reads <c>payment-events</c> and reflects each <c>PaymentReceived</c> onto the project's
/// payment status (US-24 AC-4).
/// </summary>
/// <remarks>
/// The money stays in Payment Service. This keeps one derived fact locally so a project can be
/// read without asking that service anything — the same arrangement by which Construction
/// Service holds its own copy of who owns a project.
/// <para>
/// Resilience follows the pattern <c>ConstructionEventsConsumer</c> established here, which in
/// turn followed Construction Service's <c>DesignEventsConsumer</c>:
/// <list type="bullet">
/// <item>A message that cannot be parsed is logged and its offset committed past — it will never
/// parse, and wedging the partition on it would stop every event behind it.</item>
/// <item>An event type this service does not handle is skipped without being read further.</item>
/// <item>A redelivery of an already-applied event is absorbed by the repository's
/// deduplication, not treated as an error.</item>
/// <item>A message whose handling fails — the database is unreachable — is <em>not</em>
/// committed, so the next poll re-reads it.</item>
/// <item>Nothing here reaches back to the Payment Service. A consume failure is this service's
/// problem alone; the publisher committed its event to its own outbox and moved on.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PaymentEventsConsumer : BackgroundService
{
    private const string Topic = "payment-events";
    private const string PaymentReceivedType = "PaymentReceived";

    /// <summary>The invoice status that means the payment settled it.</summary>
    private const string PaidInvoiceStatus = "Paid";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<PaymentEventsConsumer> _logger;

    public PaymentEventsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<PaymentEventsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The group this consumer reads under: the configured id with the topic appended.
    /// </summary>
    /// <remarks>
    /// Suffixed per topic, as every consumer in this codebase is. Every member of a Kafka
    /// consumer group is expected to subscribe to the same topics, so sharing the bare id with
    /// <see cref="ConstructionEventsConsumer"/> would have the two revoke each other's
    /// partitions on every rebalance and take turns being assigned nothing.
    /// </remarks>
    private string GroupId => $"{_options.ConsumerGroupId}-payment-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the request thread: BackgroundService runs ExecuteAsync inline during startup
        // until the first await, and the consume loop is a long-running one.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = GroupId,
            // Commit explicitly, only after a message is handled — so a crash mid-handling
            // re-delivers rather than skips.
            EnableAutoCommit = false,
            // A brand-new group reads the topic from the start, so payments announced before
            // this service first ran are not missed.
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
            // Leave the group cleanly so a restart does not wait out the session timeout before
            // its partitions are reassigned.
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
            // Shutting down mid-handling. Not committed — it re-delivers on the next start, and
            // the update is deduplicated by event id, so a retry is safe.
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Skipping a message on {TopicPartitionOffset} that could not be parsed as a BuildNexus event.",
                result.TopicPartitionOffset);

            // Commit past it: a malformed message will not parse on a retry, and holding the
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
    /// Processes one message body: a <c>PaymentReceived</c> moves the project's payment status,
    /// any other event type on the topic is a no-op.
    /// </summary>
    /// <remarks>
    /// The unit the tests drive directly. Its exception contract is what <see cref="ProcessAsync"/>
    /// maps to a commit decision: a <see cref="JsonException"/> is a message that will never
    /// parse, so the caller commits past it; anything else is treated as transient, so the caller
    /// leaves the offset uncommitted and the message is retried.
    /// </remarks>
    public async Task HandleAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("The message body was empty.");
        }

        var envelope = JsonSerializer.Deserialize<IncomingEvent>(value, IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The message body deserialised to null.");

        if (!string.Equals(envelope.EventType, PaymentReceivedType, StringComparison.Ordinal))
        {
            // InvoiceGenerated, FinalPaymentSettled — on the same topic, and neither is this
            // service's concern. Committed by the caller.
            return;
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A PaymentReceived event carried no payload object.");
        }

        var payload = envelope.Payload.Deserialize<PaymentReceivedPayload>(IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The PaymentReceived payload deserialised to null.");

        if (payload.ProjectId == Guid.Empty)
        {
            throw new JsonException("The PaymentReceived payload was missing its projectId.");
        }

        // The invoice's status decides which of the two reflected values this is. Anything the
        // Payment Service has not taught this service to read is refused rather than guessed at
        // — silently defaulting an unknown status to PartiallyPaid would understate a settled
        // invoice, and to InvoicePaid would overstate an unpaid one.
        var paymentStatus = payload.InvoiceStatus switch
        {
            PaidInvoiceStatus => ProjectPaymentStatus.InvoicePaid,
            "Pending" => ProjectPaymentStatus.PartiallyPaid,
            _ => throw new JsonException(
                $"A PaymentReceived event carried an invoice status this service does not know: "
                + $"'{payload.InvoiceStatus}'.")
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

        var applied = await projects.UpdatePaymentStatusAsync(
            payload.ProjectId,
            paymentStatus,
            envelope.EventId,
            envelope.OccurredAt.UtcDateTime);

        if (applied)
        {
            _logger.LogInformation(
                "Project {ProjectId} payment status set to {PaymentStatus} from PaymentReceived {EventId}.",
                payload.ProjectId, paymentStatus, envelope.EventId);

            return;
        }

        // Either the project does not exist here, or this exact event had already been applied —
        // the redelivery case, which is a success rather than a conflict. Both are committed past;
        // neither can be fixed by retrying.
        _logger.LogInformation(
            "PaymentReceived {EventId} for project {ProjectId} changed nothing: already applied, "
            + "or no such project in this service.",
            envelope.EventId, payload.ProjectId);
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
