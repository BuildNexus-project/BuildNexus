namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Turns the service's own <see cref="DateTime"/> values into the offset-bearing
/// form events go out with.
/// </summary>
internal static class EventTimestamp
{
    /// <summary>
    /// Stamps the value as UTC before it becomes a
    /// <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <remarks>
    /// These times are always UTC — the service reads <c>DateTime.UtcNow</c> and
    /// nothing else — but MySQL's <c>DATETIME</c> carries no offset, so a value
    /// read back out of the database arrives as
    /// <see cref="DateTimeKind.Unspecified"/>. Converting that implicitly would
    /// have <see cref="DateTimeOffset"/> apply the <em>server's local</em>
    /// offset, and the event would claim a time that never happened on any
    /// machine not set to UTC. Relabelling is correct here rather than merely
    /// convenient, precisely because the value already is UTC.
    /// </remarks>
    internal static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
