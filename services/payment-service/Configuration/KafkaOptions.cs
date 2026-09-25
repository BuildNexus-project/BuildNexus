namespace BuildNexus.PaymentService.Configuration;

/// <summary>
/// Settings for the Kafka consumers and the producer, bound from the
/// <c>Kafka</c> configuration section.
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
    /// The base consumer group id. Each consumer appends the topic it reads to
    /// this, rather than sharing the bare value.
    /// </summary>
    /// <remarks>
    /// Every member of a Kafka consumer group is expected to subscribe to the
    /// same topics; two members of one group subscribing to different topics
    /// make each rebalance revoke the other's partitions, and the two take turns
    /// being assigned nothing. A group per topic keeps each consumer's offsets
    /// and assignment its own — which matters here from the start, because this
    /// service reads two topics.
    /// </remarks>
    public string ConsumerGroupId { get; set; } = "payment-service";

    /// <summary>
    /// How long a publish may spend trying to reach the broker before it is
    /// given up on, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Added with US-16, the first story in which this service publishes rather
    /// than only consuming. Deliberately far below librdkafka's own five-minute
    /// default, for the same reason the Project, Design and Construction Services
    /// set it: the dispatcher sends one event at a time, so an unreachable broker
    /// should not hold up the whole queue for five minutes per event.
    /// </remarks>
    public int MessageTimeoutMs { get; set; } = 5000;
}
