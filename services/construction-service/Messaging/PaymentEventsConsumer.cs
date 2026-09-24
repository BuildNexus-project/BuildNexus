using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// Reads <c>payment-events</c> and records, for each <c>FinalPaymentSettled</c>, that
/// a project's final payment has landed — AC-4's handover precondition.
/// </summary>
/// <remarks>
/// The same arrangement <see cref="DesignEventsConsumer"/> uses for design approval:
/// a fact another service owns is replicated into this one's schema off a topic, so
/// the gate that needs it is a query in the same transaction as the write rather than
/// a synchronous call that could fail or lie.
/// <para>
/// !!! The Payment Service does not exist yet. !!! Until it publishes
/// <c>FinalPaymentSettled</c> on <c>payment-events</c>, this consumer reads a topic
/// nobody writes to, <c>payment_settlements</c> stays empty, and handover is refused
/// for every project. That is the correct direction to fail — the alternative would
/// be handing projects over unpaid — but it does mean AC-4 cannot be demonstrated
/// end to end until that service ships. The event name and payload here are this
/// story's proposal and need confirming with whoever builds it.
/// </para>
/// <para>
/// Resilience is exactly <see cref="DesignEventsConsumer"/>'s: an unparseable message
/// is committed past, a handling failure is left uncommitted to retry, and the insert
/// is idempotent so a redelivery is safe.
/// </para>
/// </remarks>
public sealed class PaymentEventsConsumer : BackgroundService
{
    private const string Topic = "payment-events";
    private const string FinalPaymentSettledType = "FinalPaymentSettled";

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the request thread: BackgroundService runs ExecuteAsync inline during
        // startup until the first await, and the consume loop is a long-running one.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            // Commit explicitly, only after a message is handled — so a crash
            // mid-handling re-delivers rather than skips.
            EnableAutoCommit = false,
            // A brand-new group reads the topic from the start, so settlements
            // published before this service first ran are not missed. That matters
            // more here than elsewhere: a settlement missed is a project that can
            // never be handed over.
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
            // Leave the group cleanly so a restart does not wait out the session
            // timeout before its partitions are reassigned.
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
            // Shutting down mid-handling. Not committed — it re-delivers on the next
            // start, and the insert is idempotent.
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
    /// Processes one message body: a <c>FinalPaymentSettled</c> becomes a settlement
    /// marker, any other event type on the topic is a no-op.
    /// </summary>
    /// <remarks>
    /// The unit the tests drive directly. Its exception contract is what
    /// <see cref="ProcessAsync"/> maps to a commit decision: a
    /// <see cref="JsonException"/> is a message that will never parse, so the caller
    /// commits past it; anything else is treated as transient, so the caller leaves
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

        if (!string.Equals(envelope.EventType, FinalPaymentSettledType, StringComparison.Ordinal))
        {
            // An instalment, an invoice raised, a refund — on the same topic, not this
            // service's concern. Committed by the caller. Note this deliberately does
            // not match a plain "PaymentSettled": AC-4 gates on the *final* payment,
            // and treating a part payment as one would hand a project over half paid.
            return;
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A FinalPaymentSettled event carried no payload object.");
        }

        var payload = envelope.Payload.Deserialize<FinalPaymentSettledPayload>(IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The FinalPaymentSettled payload deserialised to null.");

        if (payload.ProjectId == Guid.Empty)
        {
            throw new JsonException("The FinalPaymentSettled payload was missing its projectId.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentSettlementRepository>();

        var recorded = await repository.RecordSettlementIfAbsentAsync(
            payload.ProjectId,
            envelope.EventId,
            payload.SettledAt.UtcDateTime,
            cancellationToken);

        if (recorded)
        {
            _logger.LogInformation(
                "Recorded the final payment settlement for project {ProjectId} from FinalPaymentSettled {EventId}.",
                payload.ProjectId, envelope.EventId);
        }
        else
        {
            _logger.LogInformation(
                "Project {ProjectId} already has a settlement recorded; FinalPaymentSettled {EventId} absorbed.",
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
