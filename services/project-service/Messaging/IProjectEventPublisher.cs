using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Publishes this service's business events onto the message bus.
/// </summary>
/// <remarks>
/// One method per event the stories have actually asked for, rather than a
/// general "publish anything" call: the set of events on <c>project-events</c>
/// is a contract with four other services, and it should not be possible to add
/// to it by accident.
/// </remarks>
public interface IProjectEventPublisher
{
    /// <summary>
    /// Publishes <see cref="ProjectEventTypes.ProjectCreated"/> for a project
    /// that has just been stored.
    /// </summary>
    /// <exception cref="Exception">
    /// The broker could not be reached in time. The caller decides what that
    /// means for the request it is serving — the project itself is already
    /// saved by the time this is called.
    /// </exception>
    Task PublishProjectCreatedAsync(Project project, CancellationToken cancellationToken = default);
}
