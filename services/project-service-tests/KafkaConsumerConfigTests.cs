using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Messaging;
using Confluent.Kafka;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// What every consumer in this service is built with: the local broker's settings when the
/// optional broker options are absent, and Azure Event Hubs' SASL_SSL settings and tuning when
/// they are present.
/// </summary>
/// <remarks>
/// The consumers used not to pass the Event Hubs settings at all — only the producer did — so
/// against Event Hubs the service could publish and could not read. All three now build their
/// config here, and this pins it without a broker: <see cref="ConsumerConfig"/> is a dictionary of
/// librdkafka keys, and that dictionary is what is asserted.
/// </remarks>
public class KafkaConsumerConfigTests
{
    private const string EventHubsBootstrapServers = "example-namespace.servicebus.windows.net:9093";

    // Shaped like a namespace connection string; the key is not a real one.
    private const string EventHubsConnectionString =
        "Endpoint=sb://example-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=not-a-real-key";

    [Fact]
    public void Without_the_optional_settings_a_consumer_gets_exactly_the_keys_it_always_had()
    {
        var config = KafkaBrokerSettings.BuildConsumerConfig(LocalOptions(), "project-service-payment-events");

        var keys = config.Select(setting => setting.Key).Order().ToList();

        // The four settings the consumers were built with before the optional options existed —
        // and no security.protocol, sasl.* or tuning key at all, so librdkafka keeps its own
        // plaintext defaults for the local broker.
        Assert.Equal(["auto.offset.reset", "bootstrap.servers", "enable.auto.commit", "group.id"], keys);
    }

    [Fact]
    public void A_consumer_reads_from_the_start_and_commits_only_when_told()
    {
        var config = KafkaBrokerSettings.BuildConsumerConfig(LocalOptions(), "g");

        Assert.Equal("g", config.GroupId);
        Assert.Equal("kafka:9092", config.BootstrapServers);
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.False(config.EnableAutoCommit);
    }

    [Fact]
    public void With_the_Event_Hubs_settings_a_consumer_authenticates_over_SASL_SSL()
    {
        var config = KafkaBrokerSettings.BuildConsumerConfig(EventHubsOptions(), "g");

        Assert.Equal(EventHubsBootstrapServers, config.BootstrapServers);
        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("$ConnectionString", config.SaslUsername);
        Assert.Equal(EventHubsConnectionString, config.SaslPassword);
    }

    [Fact]
    public void With_the_Event_Hubs_tuning_a_consumer_gets_the_documented_values()
    {
        var config = KafkaBrokerSettings.BuildConsumerConfig(EventHubsOptions(), "g");

        Assert.True(config.SocketKeepaliveEnable);
        Assert.Equal(180000, config.MetadataMaxAgeMs);
    }

    [Fact]
    public void The_producer_only_request_timeout_is_not_given_to_a_consumer()
    {
        // request.timeout.ms is a producer setting. Passing it to a consumer would be a key
        // librdkafka ignores, and the Event Hubs value is the publisher's to apply.
        var keys = KafkaBrokerSettings.BuildConsumerConfig(EventHubsOptions(), "g").Select(s => s.Key);

        Assert.DoesNotContain("request.timeout.ms", keys);
    }

    [Fact]
    public void Each_tuning_setting_is_written_only_when_it_is_set()
    {
        var options = LocalOptions();
        options.SocketKeepaliveEnable = true;

        var keys = KafkaBrokerSettings.BuildConsumerConfig(options, "g")
            .Select(setting => setting.Key).Order().ToList();

        Assert.Equal(
            ["auto.offset.reset", "bootstrap.servers", "enable.auto.commit", "group.id", "socket.keepalive.enable"],
            keys);
    }

    [Fact]
    public void The_producer_and_a_consumer_log_in_to_the_broker_the_same_way()
    {
        // The defect this closes: only the producer used to.
        var producer = KafkaProjectEventPublisher.BuildProducerConfig(EventHubsOptions());
        var consumer = KafkaBrokerSettings.BuildConsumerConfig(EventHubsOptions(), "g");

        Assert.Equal(producer.SecurityProtocol, consumer.SecurityProtocol);
        Assert.Equal(producer.SaslMechanism, consumer.SaslMechanism);
        Assert.Equal(producer.SaslUsername, consumer.SaslUsername);
        Assert.Equal(producer.SaslPassword, consumer.SaslPassword);
        Assert.Equal(producer.SocketKeepaliveEnable, consumer.SocketKeepaliveEnable);
        Assert.Equal(producer.MetadataMaxAgeMs, consumer.MetadataMaxAgeMs);
    }

    private static KafkaOptions LocalOptions() => new()
    {
        BootstrapServers = "kafka:9092",
        MessageTimeoutMs = 5000,
        ConsumerGroupId = "project-service"
    };

    private static KafkaOptions EventHubsOptions() => new()
    {
        BootstrapServers = EventHubsBootstrapServers,
        MessageTimeoutMs = 5000,
        ConsumerGroupId = "project-service",
        SecurityProtocol = SecurityProtocol.SaslSsl,
        SaslMechanism = SaslMechanism.Plain,
        SaslUsername = "$ConnectionString",
        SaslPassword = EventHubsConnectionString,
        RequestTimeoutMs = 60000,
        SocketKeepaliveEnable = true,
        MetadataMaxAgeMs = 180000
    };
}
