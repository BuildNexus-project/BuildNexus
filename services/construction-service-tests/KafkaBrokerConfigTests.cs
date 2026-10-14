using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Messaging;
using Confluent.Kafka;

namespace BuildNexus.ConstructionService.Tests;

// No broker is needed: ProducerConfig and ConsumerConfig are dictionaries of librdkafka
// keys, and those dictionaries are what gets asserted.
public class KafkaBrokerConfigTests
{
    private const string LocalBootstrapServers = "kafka:9092";
    private const string EventHubsBootstrapServers = "example-namespace.servicebus.windows.net:9093";

    // Shaped like a namespace connection string; the key is not a real one.
    private const string EventHubsConnectionString =
        "Endpoint=sb://example-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=not-a-real-key";

    [Fact]
    public void Without_the_optional_settings_the_producer_gets_exactly_the_keys_it_always_had()
    {
        var config = KafkaConstructionEventPublisher.BuildProducerConfig(LocalOptions());

        var keys = config.Select(setting => setting.Key).Order().ToList();

        Assert.Equal(["acks", "bootstrap.servers", "enable.idempotence", "message.timeout.ms"], keys);
        Assert.Equal(LocalBootstrapServers, config.BootstrapServers);
    }

    [Fact]
    public void With_the_Event_Hubs_settings_the_producer_authenticates_over_SASL_SSL_and_gets_the_tuning()
    {
        var config = KafkaConstructionEventPublisher.BuildProducerConfig(EventHubsOptions());

        Assert.Equal(EventHubsBootstrapServers, config.BootstrapServers);
        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("$ConnectionString", config.SaslUsername);
        Assert.Equal(EventHubsConnectionString, config.SaslPassword);
        Assert.Equal(60000, config.RequestTimeoutMs);
        Assert.Equal(180000, config.MetadataMaxAgeMs);
        Assert.Equal(true, config.SocketKeepaliveEnable);
    }

    [Fact]
    public void Each_consumer_keeps_its_own_group_and_manages_its_own_offsets()
    {
        var config = KafkaBrokerSettings.BuildConsumerConfig(EventHubsOptions(), "construction-service-project-events");

        Assert.Equal("construction-service-project-events", config.GroupId);
        Assert.False(config.EnableAutoCommit);
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(EventHubsConnectionString, config.SaslPassword);
    }

    [Fact]
    public void Consumer_for_the_local_broker_gets_no_security_keys()
    {
        var config = KafkaBrokerSettings.BuildConsumerConfig(LocalOptions(), "construction-service");

        Assert.Null(config.SecurityProtocol);
        Assert.Null(config.SaslMechanism);
        Assert.Null(config.SaslPassword);
    }

    [Fact]
    public void Security_settings_are_accepted_all_set_or_all_unset()
    {
        Assert.True(LocalOptions().HasConsistentSaslSettings());
        Assert.True(EventHubsOptions().HasConsistentSaslSettings());
    }

    [Fact]
    public void A_SASL_protocol_without_its_credentials_is_refused()
    {
        var options = EventHubsOptions();
        options.SaslPassword = null;

        Assert.False(options.HasConsistentSaslSettings());
    }

    [Fact]
    public void Credentials_without_a_SASL_protocol_are_refused()
    {
        var options = LocalOptions();
        options.SaslUsername = "$ConnectionString";

        Assert.False(options.HasConsistentSaslSettings());
    }

    private static KafkaOptions LocalOptions() => new()
    {
        BootstrapServers = LocalBootstrapServers,
        ConsumerGroupId = "construction-service",
        MessageTimeoutMs = 5000
    };

    private static KafkaOptions EventHubsOptions() => new()
    {
        BootstrapServers = EventHubsBootstrapServers,
        ConsumerGroupId = "construction-service",
        MessageTimeoutMs = 60000,
        SecurityProtocol = SecurityProtocol.SaslSsl,
        SaslMechanism = SaslMechanism.Plain,
        SaslUsername = "$ConnectionString",
        SaslPassword = EventHubsConnectionString,
        RequestTimeoutMs = 60000,
        SocketKeepaliveEnable = true,
        MetadataMaxAgeMs = 180000
    };
}
