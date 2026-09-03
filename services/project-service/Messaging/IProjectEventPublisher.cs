using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Puts one already-recorded event onto the message bus.
/// </summary>
/// <remarks>
/// Takes an <see cref="OutboxEvent"/> rather than a project or a status change,
/// because by the time anything publishes, the event has already been decided
/// and written down: <see cref="ProjectEvents"/> minted the envelope inside the
/// transaction that made the change. This side only carries the bytes across.
/// <para>
/// Which means nothing on the request path implements or calls this — an
/// endpoint enqueues, and <see cref="OutboxDispatcher"/> is the only caller.
/// A publish that failed used to be a lost event; now it is a row that has not
/// been sent yet.
/// </para>
/// </remarks>
public interface IProjectEventPublisher
{
    /// <summary>
    /// Sends the event's stored envelope, keyed by its project.
    /// </summary>
    /// <exception cref="Exception">
    /// The broker could not be reached in time, or refused the message. The
    /// caller records the attempt and leaves the event pending — nothing is
    /// lost by a throw here.
    /// </exception>
    Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default);
}
