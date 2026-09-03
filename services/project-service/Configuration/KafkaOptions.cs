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
}
