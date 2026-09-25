using System.Text.Json;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// Reads <c>construction-events</c> and, for each <c>ConstructionStarted</c>,
/// raises the project's construction invoice (US-15, AC-2).
/// </summary>
/// <remarks>
/// AC-2 leaves the trigger open — "automatically off a <c>ConstructionStarted</c>
/// or milestone event". This service triggers on <c>ConstructionStarted</c>,
/// because it fires exactly once per project at an unambiguous billable moment:
/// the build has formally begun. Milestone completion fires once per milestone,
/// so billing on it would need a further rule about which milestones are
/// billable and would raise several invoices where the story describes one
/// stage. Nothing about that choice is visible to the Construction Service —
/// this consumer reads an event it already publishes for US-14, and no new event
/// type is introduced on either side.
/// <para>
/// The amount billed is the project's current quotation total. The event carries
/// no figure, and inventing a deposit percentage here would be inventing a
/// commercial rule the story does not state. A project with no quotation is
/// therefore not billed — logged and skipped, since an invoice needs an amount
/// and guessing one is worse than raising none.
/// </para>
/// <para>
/// A message that cannot be parsed is logged and committed past, since it will
/// never parse and holding the partition on it would stop everything behind it;
/// a message that parses but whose handling fails is left uncommitted, so the
/// next poll retries it — and the insert is guarded by a unique index on the
/// causing event, so a retry cannot bill the project twice.
/// </para>
/// </remarks>
public sealed class ConstructionEventsConsumer : BackgroundService
{
    private const string Topic = "construction-events";
    private const string ConstructionStartedType = "ConstructionStarted";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ConstructionEventsConsumer> _logger;

    public ConstructionEventsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ConstructionEventsConsumer> logger)
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
    private string GroupId => $"{_options.ConsumerGroupId}-construction-events";

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
            // A brand-new group reads the topic from the start, so a build that
            // started before this consumer first ran is still invoiced rather
            // than silently never billed.
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
            // next start, and the insert is idempotent on the causing event.
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
    /// Processes one message body: a <c>ConstructionStarted</c> raises the
    /// project's invoice, any other event type on the topic is a no-op.
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

        if (!string.Equals(envelope.EventType, ConstructionStartedType, StringComparison.Ordinal))
        {
            // ConstructionCompleted, and whatever the topic carries later. Only
            // the start of the build is a billable stage in this story.
            // Committed by the caller.
            return;
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A ConstructionStarted event carried no payload object.");
        }

        var payload = envelope.Payload.Deserialize<ConstructionStartedPayload>(IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The ConstructionStarted payload deserialised to null.");

        if (payload.ProjectId == Guid.Empty || payload.StartedBy == Guid.Empty)
        {
            throw new JsonException("The ConstructionStarted payload was missing its projectId or startedBy.");
        }

        if (envelope.EventId == Guid.Empty)
        {
            // The eventId is what makes a redelivery a no-op. Without it this
            // could bill the project again on every retry, so it is refused
            // rather than billed without a guard.
            throw new JsonException("The ConstructionStarted envelope carried no eventId.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var quotations = scope.ServiceProvider.GetRequiredService<IQuotationRepository>();
        var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();

        var quotation = await quotations.GetCurrentForProjectAsync(payload.ProjectId, cancellationToken);

        if (quotation is null)
        {
            // Nothing to bill against. Skipped rather than failed: retrying would
            // not conjure a quotation, so holding the partition on it would stop
            // every later event for no gain. A Project Manager can still raise
            // the invoice by hand once the project is quoted.
            _logger.LogWarning(
                "Project {ProjectId} started construction with no quotation, so no invoice was raised "
                + "from ConstructionStarted {EventId}. Raise one manually once the project is quoted.",
                payload.ProjectId, envelope.EventId);

            return;
        }

        var invoice = await invoices.CreateFromEventIfAbsentAsync(
            payload.ProjectId,
            quotation.EstimatedTotal,
            payload.StartedBy,
            envelope.EventId,
            cancellationToken);

        if (invoice is not null)
        {
            _logger.LogInformation(
                "Raised invoice {InvoiceId} for {Amount} against project {ProjectId} from "
                + "ConstructionStarted {EventId}.",
                invoice.Id, invoice.Amount, payload.ProjectId, envelope.EventId);
        }
        else
        {
            _logger.LogInformation(
                "ConstructionStarted {EventId} had already raised an invoice for project {ProjectId}; absorbed.",
                envelope.EventId, payload.ProjectId);
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
