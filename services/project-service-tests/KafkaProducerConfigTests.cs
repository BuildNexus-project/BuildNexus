using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// What the producer is built with: the local broker's settings when the optional
/// broker options are absent, and Azure Event Hubs' SASL_SSL settings and
/// connection tuning when they are present.
/// </summary>
/// <remarks>
/// No broker is needed. <see cref="ProducerConfig"/> is a dictionary of
/// librdkafka keys, and that dictionary is what gets asserted — so the first test
/// pins, key for key, that adding the optional options changed nothing about a
/// local run.
/// </remarks>
public class KafkaProducerConfigTests
{
    private const string LocalBootstrapServers = "kafka:9092";
    private const string EventHubsBootstrapServers = "example-namespace.servicebus.windows.net:9093";

    // The values Event Hubs documents for librdkafka clients.
    private const int EventHubsRequestTimeoutMs = 60000;
    private const int EventHubsMetadataMaxAgeMs = 180000;

    // Shaped like a namespace connection string; the key is not a real one.
    private const string EventHubsConnectionString =
        "Endpoint=sb://example-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=not-a-real-key";

    [Fact]
    public void Without_the_optional_settings_the_producer_gets_exactly_the_keys_it_always_had()
    {
        var config = KafkaProjectEventPublisher.BuildProducerConfig(LocalOptions());

        var keys = config.Select(setting => setting.Key).Order().ToList();

        // The four settings the producer was built with before the optional
        // options existed — and no security.protocol, sasl.*, request.timeout.ms,
        // socket.keepalive.enable or metadata.max.age.ms key at all, so librdkafka
        // keeps its own defaults, plaintext included, for the local broker.
        Assert.Equal(["acks", "bootstrap.servers", "enable.idempotence", "message.timeout.ms"], keys);
        Assert.Equal(LocalBootstrapServers, config.BootstrapServers);
    }

    [Fact]
    public void With_the_Event_Hubs_settings_the_producer_authenticates_over_SASL_SSL()
    {
        var config = KafkaProjectEventPublisher.BuildProducerConfig(EventHubsOptions());

        Assert.Equal(EventHubsBootstrapServers, config.BootstrapServers);
        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("$ConnectionString", config.SaslUsername);
        Assert.Equal(EventHubsConnectionString, config.SaslPassword);
    }

    [Fact]
    public void With_the_Event_Hubs_connection_tuning_the_producer_gets_the_documented_values()
    {
        var config = KafkaProjectEventPublisher.BuildProducerConfig(EventHubsOptions());

        Assert.Equal(EventHubsRequestTimeoutMs, config.RequestTimeoutMs);
        Assert.True(config.SocketKeepaliveEnable);
        Assert.Equal(EventHubsMetadataMaxAgeMs, config.MetadataMaxAgeMs);
    }

    [Fact]
    public void Each_tuning_setting_is_written_only_when_it_is_set()
    {
        // Independent of one another and of the security settings: setting one
        // adds exactly its own key to what a local run already had.
        var options = LocalOptions();
        options.RequestTimeoutMs = EventHubsRequestTimeoutMs;

        var keys = KafkaProjectEventPublisher.BuildProducerConfig(options)
            .Select(setting => setting.Key).Order().ToList();

        Assert.Equal(["acks", "bootstrap.servers", "enable.idempotence", "message.timeout.ms", "request.timeout.ms"], keys);
    }

    [Fact]
    public void The_optional_settings_leave_the_delivery_guarantees_as_they_were()
    {
        var local = KafkaProjectEventPublisher.BuildProducerConfig(LocalOptions());
        var eventHubs = KafkaProjectEventPublisher.BuildProducerConfig(EventHubsOptions());

        Assert.Equal(local.Acks, eventHubs.Acks);
        Assert.Equal(local.EnableIdempotence, eventHubs.EnableIdempotence);
        Assert.Equal(local.MessageTimeoutMs, eventHubs.MessageTimeoutMs);
    }

    [Fact]
    public void The_deployed_setting_values_bind_onto_the_options()
    {
        // The strings a deployment supplies as Kafka__* environment variables. An
        // enum value that failed to convert would stop the host at startup.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = EventHubsBootstrapServers,
                ["Kafka:SecurityProtocol"] = "SaslSsl",
                ["Kafka:SaslMechanism"] = "Plain",
                ["Kafka:SaslUsername"] = "$ConnectionString",
                ["Kafka:SaslPassword"] = EventHubsConnectionString,
                ["Kafka:RequestTimeoutMs"] = "60000",
                ["Kafka:SocketKeepaliveEnable"] = "true",
                ["Kafka:MetadataMaxAgeMs"] = "180000"
            })
            .Build();

        var options = configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>()!;

        Assert.Equal(SecurityProtocol.SaslSsl, options.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, options.SaslMechanism);
        Assert.Equal("$ConnectionString", options.SaslUsername);
        Assert.Equal(EventHubsConnectionString, options.SaslPassword);
        Assert.Equal(EventHubsRequestTimeoutMs, options.RequestTimeoutMs);
        Assert.True(options.SocketKeepaliveEnable);
        Assert.Equal(EventHubsMetadataMaxAgeMs, options.MetadataMaxAgeMs);
        Assert.True(options.HasConsistentSaslSettings());
    }

    [Fact]
    public void No_security_settings_at_all_is_consistent()
    {
        Assert.True(LocalOptions().HasConsistentSaslSettings());
    }

    [Fact]
    public void The_full_Event_Hubs_settings_are_consistent()
    {
        Assert.True(EventHubsOptions().HasConsistentSaslSettings());
    }

    [Fact]
    public void A_non_SASL_protocol_on_its_own_is_consistent()
    {
        var options = LocalOptions();
        options.SecurityProtocol = SecurityProtocol.Ssl;

        Assert.True(options.HasConsistentSaslSettings());
    }

    [Fact]
    public void SASL_details_without_a_SASL_protocol_are_refused()
    {
        // Would otherwise be ignored, leaving the producer on plaintext against a
        // broker that only speaks SASL over TLS.
        var options = EventHubsOptions();
        options.SecurityProtocol = null;

        Assert.False(options.HasConsistentSaslSettings());
    }

    [Theory]
    [InlineData("mechanism")]
    [InlineData("username")]
    [InlineData("password")]
    public void A_SASL_protocol_missing_any_detail_is_refused(string missing)
    {
        var options = EventHubsOptions();
        switch (missing)
        {
            case "mechanism":
                options.SaslMechanism = null;
                break;
            case "username":
                options.SaslUsername = null;
                break;
            case "password":
                options.SaslPassword = "";
                break;
        }

        Assert.False(options.HasConsistentSaslSettings());
    }

    private static KafkaOptions LocalOptions() => new() { BootstrapServers = LocalBootstrapServers };

    private static KafkaOptions EventHubsOptions() => new()
    {
        BootstrapServers = EventHubsBootstrapServers,
        SecurityProtocol = SecurityProtocol.SaslSsl,
        SaslMechanism = SaslMechanism.Plain,
        SaslUsername = "$ConnectionString",
        SaslPassword = EventHubsConnectionString,
        RequestTimeoutMs = EventHubsRequestTimeoutMs,
        SocketKeepaliveEnable = true,
        MetadataMaxAgeMs = EventHubsMetadataMaxAgeMs
    };
}
