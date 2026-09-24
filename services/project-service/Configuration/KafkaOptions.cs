namespace BuildNexus.ProjectService.Configuration;

/// <summary>
/// Settings for the Kafka producer, bound from the <c>Kafka</c> configuration
/// section.
/// </summary>
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>
    /// The broker list, supplied out of band as <c>Kafka__BootstrapServers</c>
    /// — the shared name every publishing service uses.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// How long a publish may spend trying to reach the broker before it is
    /// given up on, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Deliberately far below librdkafka's own five-minute default. The
    /// dispatcher sends events one at a time and in order, so a broker that is
    /// unreachable would otherwise hold the whole queue for five minutes per
    /// event before it could record the failure and try again. Five seconds
    /// bounds that, and an attempt that misses the window costs nothing: the
    /// row stays pending and the next pass picks it up.
    /// </remarks>
    public int MessageTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// The consumer group this service reads <c>construction-events</c> under.
    /// </summary>
    /// <remarks>
    /// Added with US-14, the first story in which this service consumes as well as
    /// publishes. One group means one logical reader: restarts and multiple replicas
    /// share the partitions and the committed offset rather than each replaying the
    /// topic and each moving the same project again.
    /// </remarks>
    public string ConsumerGroupId { get; set; } = "project-service";

    /// <summary>
    /// How the producer connects to the broker, supplied as
    /// <c>Kafka__SecurityProtocol</c>: <c>SaslSsl</c> for Azure Event Hubs, left
    /// unset for the local broker.
    /// </summary>
    /// <remarks>
    /// This and the three SASL settings below are optional, and absent from
    /// every local configuration. Unset, the producer is given none of them and
    /// librdkafka speaks plaintext — what the compose broker's listener expects —
    /// exactly as it did before they existed. Event Hubs accepts nothing but
    /// SASL over TLS, so a deployment sets all four; see
    /// <see cref="HasConsistentSaslSettings"/> for why it is all four or none.
    /// </remarks>
    public Confluent.Kafka.SecurityProtocol? SecurityProtocol { get; set; }

    /// <summary>
    /// The SASL mechanism, supplied as <c>Kafka__SaslMechanism</c>. <c>Plain</c>
    /// for an Event Hubs connection string.
    /// </summary>
    public Confluent.Kafka.SaslMechanism? SaslMechanism { get; set; }

    /// <summary>
    /// The SASL username, supplied as <c>Kafka__SaslUsername</c>. For Event Hubs
    /// this is the literal text <c>$ConnectionString</c> — not a placeholder to
    /// substitute.
    /// </summary>
    public string? SaslUsername { get; set; }

    /// <summary>
    /// The SASL password, supplied as <c>Kafka__SaslPassword</c>. For Event Hubs,
    /// the namespace's connection string. A secret: it belongs in the
    /// environment, never in <c>appsettings.json</c>.
    /// </summary>
    public string? SaslPassword { get; set; }

    /// <summary>
    /// How long the broker may take to acknowledge a produce request, in
    /// milliseconds, supplied as <c>Kafka__RequestTimeoutMs</c>. Unset locally.
    /// </summary>
    /// <remarks>
    /// This and the two connection settings below are optional tuning for Azure
    /// Event Hubs, and absent from every local configuration: unset, the producer
    /// is given none of them and keeps librdkafka's own defaults, exactly as it
    /// did before they existed. Event Hubs documents librdkafka's five-second
    /// default as too low for it — it enforces a twenty-second minimum
    /// internally — and recommends 60000.
    /// </remarks>
    public int? RequestTimeoutMs { get; set; }

    /// <summary>
    /// Whether to send TCP keepalives on broker connections, supplied as
    /// <c>Kafka__SocketKeepaliveEnable</c>. Unset locally.
    /// </summary>
    /// <remarks>
    /// Azure closes a connection that has been idle for 240 seconds, and Event
    /// Hubs documents keepalives as required to prevent it. A producer with
    /// nothing to publish for a few minutes is exactly that idle connection.
    /// </remarks>
    public bool? SocketKeepaliveEnable { get; set; }

    /// <summary>
    /// How often to refresh broker and partition metadata, in milliseconds,
    /// supplied as <c>Kafka__MetadataMaxAgeMs</c>. Unset locally.
    /// </summary>
    /// <remarks>
    /// Event Hubs requires this below the same 240-second idle limit and
    /// recommends 180000; librdkafka's own default is fifteen minutes.
    /// </remarks>
    public int? MetadataMaxAgeMs { get; set; }

    /// <summary>
    /// Whether the security settings are all-or-nothing, as they have to be.
    /// </summary>
    /// <remarks>
    /// Half a configuration fails quietly either way round. SASL details with no
    /// SASL protocol are simply ignored, and the producer tries plaintext against
    /// a broker that only speaks SASL over TLS. A SASL protocol with no mechanism
    /// falls back to librdkafka's GSSAPI default, which Event Hubs does not offer.
    /// Either one surfaces as publishes failing at the connection rather than as
    /// a configuration error — so the service refuses to start instead.
    /// </remarks>
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
