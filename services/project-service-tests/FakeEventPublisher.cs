using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// Stands in for the broker: records what it was asked to send, and refuses
/// whatever a test tells it to.
/// </summary>
/// <remarks>
/// What the dispatcher needs a publisher to be. A real Kafka producer would
/// make these tests depend on a broker being up and on how long it took to
/// answer, and neither is what is under test — the question here is what the
/// dispatcher does with a success and with a failure.
/// </remarks>
public sealed class FakeEventPublisher : IProjectEventPublisher
{
    private readonly object _gate = new();
    private readonly List<OutboxEvent> _attempts = [];

    /// <summary>
    /// Which events the broker refuses. Everything gets through by default.
    /// </summary>
    public Func<OutboxEvent, bool> Refuses { get; set; } = _ => false;

    /// <summary>
    /// Every event this was asked to send, in order, failures included —
    /// which is what makes "it never tried the second one" assertable.
    /// </summary>
    public IReadOnlyList<OutboxEvent> Attempts
    {
        get
        {
            lock (_gate)
            {
                return [.. _attempts];
            }
        }
    }

    /// <summary>The ones it actually took.</summary>
    public IReadOnlyList<OutboxEvent> Accepted
    {
        get
        {
            lock (_gate)
            {
                return [.. _attempts.Where(e => !Refuses(e))];
            }
        }
    }

    public Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        // Locked because the dispatcher runs on its own thread while the test
        // reads these, and a List being appended to while it is enumerated
        // throws rather than merely racing.
        lock (_gate)
        {
            _attempts.Add(outboxEvent);
        }

        return Refuses(outboxEvent)
            // What an unreachable broker looks like from the dispatcher's side.
            ? Task.FromException(new InvalidOperationException("The broker could not be reached."))
            : Task.CompletedTask;
    }
}
