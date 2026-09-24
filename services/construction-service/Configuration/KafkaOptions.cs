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
}
