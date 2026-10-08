using BuildNexus.ProjectService.Configuration;
using Confluent.Kafka;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The librdkafka settings every Kafka client in this service shares: how to authenticate to the
/// broker and how to tune the connection.
/// </summary>
/// <remarks>
/// One place, so the producer and every consumer talk to the broker the same way. They did not:
/// only the producer passed the Azure Event Hubs settings, so against Event Hubs it could publish
/// while the consumers could not even connect. Written once here, a client added later cannot
/// forget them.
/// </remarks>
public static class KafkaBrokerSettings
{
    /// <summary>
    /// How often a consumer asks the broker whether a topic it subscribed to has appeared.
    /// </summary>
    /// <remarks>
    /// A topic only exists once something publishes to it, so on a fresh stack a consumer
    /// subscribes to topics that are not there yet. librdkafka then re-checks for them only every
    /// <c>topic.metadata.refresh.interval.ms</c>, which defaults to five minutes, and until it does
    /// the topic is silently left out of the subscription: the events are published and nothing
    /// reads them. The <c>.fast</c> refresh settings do not help — they only speed up recovery from
    /// a lost partition leader, not discovery of a topic that has never existed. Ten seconds keeps
    /// the gap short for the cost of one small metadata request per consumer per interval.
    /// </remarks>
    private const int TopicMetadataRefreshIntervalMs = 10_000;

    /// <summary>
    /// What a consumer in <paramref name="groupId"/> is built with.
    /// </summary>
    /// <remarks>
    /// Separate from the consumer classes so the settings can be checked without building a
    /// consumer, which would start librdkafka's background threads and try to reach a broker.
    /// </remarks>
    public static ConsumerConfig BuildConsumerConfig(KafkaOptions options, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = groupId,
            // Commit explicitly, only after a message is handled — so a crash mid-handling
            // re-delivers rather than skips.
            EnableAutoCommit = false,
            // A brand-new group reads each topic from the start, so an event announced before
            // this reader first ran is not missed.
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Notice a subscribed topic that did not exist at startup within seconds, not minutes.
            TopicMetadataRefreshIntervalMs = TopicMetadataRefreshIntervalMs
        };

        ApplyBrokerSettings(options, config);

        return config;
    }

    /// <summary>
    /// Writes the optional broker settings onto <paramref name="config"/>, each only when
    /// configured.
    /// </summary>
    /// <remarks>
    /// Unset, none of these keys reaches librdkafka and it keeps its own plaintext defaults,
    /// which is what the local broker speaks: a local run's client is configured exactly as it
    /// was before these settings existed. Azure Event Hubs sets all four security settings —
    /// SaslSsl, Plain, the literal username <c>$ConnectionString</c>, and the namespace
    /// connection string as the password — and the tuning values, which are independent of one
    /// another and of the security settings.
    /// <para>
    /// <c>request.timeout.ms</c> is not here: it is a producer-only setting, so the publisher
    /// applies it to its own config and a consumer is never given a key it would ignore.
    /// </para>
    /// </remarks>
    public static void ApplyBrokerSettings(KafkaOptions options, ClientConfig config)
    {
        if (options.SecurityProtocol is { } securityProtocol)
        {
            config.SecurityProtocol = securityProtocol;
        }

        if (options.SaslMechanism is { } saslMechanism)
        {
            config.SaslMechanism = saslMechanism;
        }

        if (!string.IsNullOrEmpty(options.SaslUsername))
        {
            config.SaslUsername = options.SaslUsername;
        }

        if (!string.IsNullOrEmpty(options.SaslPassword))
        {
            config.SaslPassword = options.SaslPassword;
        }

        if (options.SocketKeepaliveEnable is { } socketKeepaliveEnable)
        {
            config.SocketKeepaliveEnable = socketKeepaliveEnable;
        }

        if (options.MetadataMaxAgeMs is { } metadataMaxAgeMs)
        {
            config.MetadataMaxAgeMs = metadataMaxAgeMs;
        }
    }
}
