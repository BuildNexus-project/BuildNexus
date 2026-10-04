using BuildNexus.ConstructionService.Configuration;
using Confluent.Kafka;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// The librdkafka settings every Kafka client in this service shares: how to authenticate to
/// the broker and how to tune the connection. Written once so the producer and all three
/// consumers talk to the broker the same way.
/// </summary>
public static class KafkaBrokerSettings
{
    public static ConsumerConfig BuildConsumerConfig(KafkaOptions options, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        ApplyBrokerSettings(options, config);

        return config;
    }

    /// <summary>
    /// Writes each optional broker setting onto <paramref name="config"/> only when configured.
    /// Unset, none of these keys reaches librdkafka, which keeps its plaintext defaults for the
    /// local broker.
    /// </summary>
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
