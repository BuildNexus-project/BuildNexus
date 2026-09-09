namespace BuildNexus.DesignService.Configuration;

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
    /// Deliberately far below librdkafka's own five-minute default — see
    /// <c>project-service</c>'s identical option for the full reasoning: the
    /// dispatcher sends one event at a time, so an unreachable broker should
    /// not hold up the whole queue for five minutes per event.
    /// </remarks>
    public int MessageTimeoutMs { get; set; } = 5000;
}
