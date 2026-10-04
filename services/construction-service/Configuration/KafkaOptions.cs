namespace BuildNexus.ConstructionService.Configuration;

/// <summary>
/// Settings for the Kafka consumer, bound from the <c>Kafka</c> configuration
/// section.
/// </summary>
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>
    /// The broker list, supplied out of band as <c>Kafka__BootstrapServers</c>
    /// — the shared name every service that touches Kafka uses.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// The consumer group this service reads <c>design-events</c> under. One
    /// group means one logical reader: restarts and multiple replicas share the
    /// partitions and the committed offset rather than each replaying the topic.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "construction-service";

    /// <summary>
    /// How long a publish may spend trying to reach the broker before it is given
    /// up on, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Added with US-14, the first story in which this service publishes rather than
    /// only consuming. Deliberately far below librdkafka's own five-minute default,
    /// for the same reason the Project and Design Services set it: the dispatcher
    /// sends one event at a time, so an unreachable broker should not hold up the
    /// whole queue for five minutes per event.
    /// </remarks>
    public int MessageTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// How clients connect to the broker, supplied as <c>Kafka__SecurityProtocol</c>:
    /// <c>SaslSsl</c> for Azure Event Hubs, unset for the local broker.
    /// </summary>
    /// <remarks>
    /// The four broker security settings are all-or-nothing; see
    /// <see cref="HasConsistentSaslSettings"/>.
    /// </remarks>
    public Confluent.Kafka.SecurityProtocol? SecurityProtocol { get; set; }

    /// <summary>The SASL mechanism, <c>Kafka__SaslMechanism</c>. <c>Plain</c> for Event Hubs.</summary>
    public Confluent.Kafka.SaslMechanism? SaslMechanism { get; set; }

    /// <summary>The SASL username, <c>Kafka__SaslUsername</c>. Literal <c>$ConnectionString</c> for Event Hubs.</summary>
    public string? SaslUsername { get; set; }

    /// <summary>The SASL password, <c>Kafka__SaslPassword</c>. The namespace connection string for Event Hubs.</summary>
    public string? SaslPassword { get; set; }

    /// <summary>Producer request timeout, <c>Kafka__RequestTimeoutMs</c>. Event Hubs recommends 60000.</summary>
    public int? RequestTimeoutMs { get; set; }

    /// <summary>TCP keepalive on broker connections, <c>Kafka__SocketKeepaliveEnable</c>. Event Hubs requires it.</summary>
    public bool? SocketKeepaliveEnable { get; set; }

    /// <summary>Metadata refresh interval, <c>Kafka__MetadataMaxAgeMs</c>. Event Hubs recommends 180000.</summary>
    public int? MetadataMaxAgeMs { get; set; }

    /// <summary>
    /// Whether the security settings are all set or all unset. Half a configuration
    /// otherwise fails quietly as publishes that cannot connect.
    /// </summary>
    public bool HasConsistentSaslSettings()
    {
        var usesSasl = SecurityProtocol is Confluent.Kafka.SecurityProtocol.SaslSsl
            or Confluent.Kafka.SecurityProtocol.SaslPlaintext;

        return usesSasl
            ? SaslMechanism is not null
                && !string.IsNullOrEmpty(SaslUsername)
                && !string.IsNullOrEmpty(SaslPassword)
            : SaslMechanism is null
                && string.IsNullOrEmpty(SaslUsername)
                && string.IsNullOrEmpty(SaslPassword);
    }
}
