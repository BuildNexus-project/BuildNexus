using BuildNexus.DesignService.Configuration;
using BuildNexus.DesignService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// Publishes this service's events to Kafka on the <c>design-events</c> topic.
/// </summary>
/// <remarks>
/// One topic per publishing service, not one per event type — see
/// <c>project-service</c>'s identical publisher for the full reasoning: a
/// consumer subscribes to everything this service has to say and filters on
/// the envelope's <c>eventType</c>.
/// </remarks>
public sealed class KafkaDesignEventPublisher : IDesignEventPublisher, IDisposable
{
    /// <summary>The one topic this service publishes to.</summary>
    public const string Topic = "design-events";

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaDesignEventPublisher> _logger;
    private readonly TimeSpan _flushTimeout;

    public KafkaDesignEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaDesignEventPublisher> logger)
    {
        var kafkaOptions = options.Value;
        _logger = logger;
        _flushTimeout = TimeSpan.FromMilliseconds(kafkaOptions.MessageTimeoutMs);

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            // Wait for every in-sync replica before calling a publish done. An
            // event that only reached the leader can still be lost if that
            // broker fails, and a lost DesignApproved means the other services
            // never learn the design was signed off.
            Acks = Acks.All,
            // A retry inside librdkafka must not put the same event on the topic
            // twice; idempotence makes the broker recognise the duplicate.
            EnableIdempotence = true,
            // Bounded, because the publish is awaited inside an HTTP request.
            MessageTimeoutMs = kafkaOptions.MessageTimeoutMs
        }).Build();
    }

    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string>
        {
            // Keyed by document id, so every event about one document lands on
            // the same partition and reaches consumers in the order it
            // happened.
            Key = outboxEvent.DocumentId.ToString(),
            // The envelope exactly as it was serialised when the change was
            // made. Not rebuilt here — see project-service's identical
            // publisher for why that matters to a deduplicating consumer.
            Value = outboxEvent.Envelope
        };

        var result = await _producer.ProduceAsync(Topic, message, cancellationToken);

        _logger.LogInformation(
            "Published {EventType} {EventId} for document {DocumentId} to {TopicPartitionOffset}.",
            outboxEvent.EventType,
            outboxEvent.Id,
            outboxEvent.DocumentId,
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
