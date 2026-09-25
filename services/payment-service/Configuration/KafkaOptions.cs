namespace BuildNexus.PaymentService.Configuration;

/// <summary>
/// Settings for the Kafka consumers, bound from the <c>Kafka</c> configuration
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
}
