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
    /// Deliberately far below librdkafka's own five-minute default. The publish
    /// is awaited inside the HTTP request that caused it, so with the default a
    /// Client submitting a project while the broker was down would sit watching
    /// a spinner for five minutes. Five seconds bounds that, and a publish that
    /// misses the window is logged rather than failing the request — the
    /// project itself is already saved.
    /// </remarks>
    public int MessageTimeoutMs { get; set; } = 5000;
}
