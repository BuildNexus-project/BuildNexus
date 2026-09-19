using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Publishes this service's events to Kafka on the <c>project-events</c> topic.
/// </summary>
/// <remarks>
/// One topic per publishing service, not one per event type: a consumer
/// subscribes to everything the Project Service has to say and filters on the
/// envelope's <c>eventType</c>. A topic per event type would multiply
/// partitions for no gain and lose the ordering between two events about the
/// same project.
/// </remarks>
public sealed class KafkaProjectEventPublisher : IProjectEventPublisher, IDisposable
{
    /// <summary>The one topic this service publishes to.</summary>
    public const string Topic = "project-events";

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProjectEventPublisher> _logger;
    private readonly TimeSpan _flushTimeout;

    public KafkaProjectEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaProjectEventPublisher> logger)
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
    public static ProducerConfig BuildProducerConfig(KafkaOptions kafkaOptions)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            // Wait for every in-sync replica before calling a publish done. An
            // event that only reached the leader can still be lost if that
            // broker fails, and a lost ProjectCreated means the other services
            // never learn the project exists.
            Acks = Acks.All,
            // A retry inside librdkafka must not put the same event on the topic
            // twice; idempotence makes the broker recognise the duplicate.
            EnableIdempotence = true,
            // Bounded, because the publish is awaited inside an HTTP request.
            // See the remarks on KafkaOptions.MessageTimeoutMs.
            MessageTimeoutMs = kafkaOptions.MessageTimeoutMs
        };

        // How to authenticate to the broker — each written only when configured.
        // Unset, none of these keys reaches librdkafka and it keeps its own
        // plaintext default, which is what the local broker speaks: a local run's
        // producer is configured exactly as it was before these settings existed.
        // Azure Event Hubs sets all four — SaslSsl, Plain, the literal username
        // $ConnectionString, and the namespace connection string as the password.
        if (kafkaOptions.SecurityProtocol is { } securityProtocol)
        {
            config.SecurityProtocol = securityProtocol;
        }

        if (kafkaOptions.SaslMechanism is { } saslMechanism)
        {
            config.SaslMechanism = saslMechanism;
        }

        if (!string.IsNullOrEmpty(kafkaOptions.SaslUsername))
        {
            config.SaslUsername = kafkaOptions.SaslUsername;
        }

        if (!string.IsNullOrEmpty(kafkaOptions.SaslPassword))
        {
            config.SaslPassword = kafkaOptions.SaslPassword;
        }

        // Connection tuning for Azure Event Hubs — likewise each written only when
        // configured, so locally librdkafka keeps its own defaults. Independent of
        // one another and of the security settings above.
        if (kafkaOptions.RequestTimeoutMs is { } requestTimeoutMs)
        {
            config.RequestTimeoutMs = requestTimeoutMs;
        }

        if (kafkaOptions.SocketKeepaliveEnable is { } socketKeepaliveEnable)
        {
            config.SocketKeepaliveEnable = socketKeepaliveEnable;
        }

        if (kafkaOptions.MetadataMaxAgeMs is { } metadataMaxAgeMs)
        {
            config.MetadataMaxAgeMs = metadataMaxAgeMs;
        }

        return config;
    }

    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string>
        {
            // Keyed by project id, so every event about one project lands on the
            // same partition and reaches consumers in the order it happened.
            Key = outboxEvent.ProjectId.ToString(),
            // The envelope exactly as it was serialised when the change was
            // made. Not rebuilt here: a retry must put the same eventId and the
            // same occurredAt on the topic, or a consumer deduplicating on the
            // id would see every attempt as a new event.
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
