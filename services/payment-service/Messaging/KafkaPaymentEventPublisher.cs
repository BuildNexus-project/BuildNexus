using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// Publishes this service's events to Kafka on the <c>payment-events</c>
/// topic.
/// </summary>
/// <remarks>
/// One topic per publishing service, not one per event type: a consumer subscribes
/// to everything the Payment Service has to say and filters on the envelope's
/// <c>eventType</c> — the same way this service's own consumers read
/// <c>construction-events</c> and <c>project-events</c>. A topic per event type
/// would multiply partitions for no gain and lose the ordering between two events about
/// the same project.
/// </remarks>
public sealed class KafkaPaymentEventPublisher : IPaymentEventPublisher, IDisposable
{
    /// <summary>The one topic this service publishes to.</summary>
    public const string Topic = "payment-events";

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaPaymentEventPublisher> _logger;
    private readonly TimeSpan _flushTimeout;

    public KafkaPaymentEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaPaymentEventPublisher> logger)
    {
        var kafkaOptions = options.Value;
        _logger = logger;
        _flushTimeout = TimeSpan.FromMilliseconds(kafkaOptions.MessageTimeoutMs);

        _producer = new ProducerBuilder<string, string>(BuildProducerConfig(kafkaOptions)).Build();
    }

    /// <summary>
    /// The librdkafka settings the producer is built with.
    /// </summary>
    /// <remarks>
    /// Separate from the constructor so the settings can be checked without
    /// building a producer, which would start librdkafka's background threads.
    /// </remarks>
    public static ProducerConfig BuildProducerConfig(KafkaOptions kafkaOptions) => new()
    {
        BootstrapServers = kafkaOptions.BootstrapServers,
        // Wait for every in-sync replica before calling a publish done. An event
        // that only reached the leader can still be lost if that broker fails, and
        // a lost PaymentReceived means a payment the rest of the platform never
        // hears about.
        Acks = Acks.All,
        // A retry inside librdkafka must not put the same event on the topic twice;
        // idempotence makes the broker recognise the duplicate.
        EnableIdempotence = true,
        // Bounded — see the remarks on KafkaOptions.MessageTimeoutMs.
        MessageTimeoutMs = kafkaOptions.MessageTimeoutMs
    };

    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string>
        {
            // Keyed by project id, so every event about one project lands on the
            // same partition and reaches consumers in the order it happened.
            Key = outboxEvent.ProjectId.ToString(),
            // The envelope exactly as it was serialised when the transition was
            // made. Not rebuilt here: a retry must put the same eventId and the
            // same occurredAt on the topic, or a consumer deduplicating on the id
            // would see every attempt as a new event.
            Value = outboxEvent.Envelope
        };

        var result = await _producer.ProduceAsync(Topic, message, cancellationToken);

        _logger.LogInformation(
            "Published {EventType} {EventId} for project {ProjectId} to {TopicPartitionOffset}.",
            outboxEvent.EventType,
            outboxEvent.Id,
            outboxEvent.ProjectId,
            result.TopicPartitionOffset);
    }

    /// <summary>
    /// Flushes anything still in the producer's buffer before the process goes
    /// away, so an event published moments before shutdown is not dropped.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _producer.Flush(_flushTimeout);
        }
        catch (Exception ex)
        {
            // Shutdown is already under way; there is nothing left to fail, and
            // throwing here would only mask whatever is really stopping us.
            _logger.LogError(ex, "Failed to flush pending events while shutting down.");
        }

        _producer.Dispose();
    }
}
