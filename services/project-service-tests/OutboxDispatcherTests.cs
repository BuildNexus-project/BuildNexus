using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The dispatcher's side of the first US-22 acceptance bullet: what actually
/// makes a publish reliable once the transaction has committed.
/// </summary>
/// <remarks>
/// The real <see cref="OutboxDispatcher"/> is started and stopped, so the loop
/// under test is the one that runs in production rather than a method lifted out
/// of it. The broker and the table are stand-ins — neither is what these cover.
/// <para>
/// Nothing here sleeps for a fixed period. Each test waits on the condition it
/// cares about and gives up after a few seconds, so a passing run is as quick as
/// the dispatcher is and a broken one fails rather than hanging.
/// </para>
/// </remarks>
public class OutboxDispatcherTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");

    [Fact]
    public async Task Sends_a_pending_event_and_records_the_delivery()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        outbox.Seed(Pending(1));

        await RunUntil(outbox, publisher, () => publisher.Attempts.Count == 1);

        var sent = Assert.Single(outbox.Events);
        Assert.True(sent.IsPublished);
        Assert.Equal(1, sent.AttemptCount);
        Assert.Null(sent.LastError);
    }

    [Fact]
    public async Task Sends_the_envelope_exactly_as_it_was_stored()
    {
        // Not rebuilt at dispatch: a retry has to put the same eventId and the
        // same occurredAt on the topic, or a consumer deduplicating on the id
        // reads every attempt as a new event.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        var pending = Pending(1);
        outbox.Seed(pending);

        await RunUntil(outbox, publisher, () => publisher.Attempts.Count == 1);

        var sent = Assert.Single(publisher.Attempts);
        Assert.Equal(pending.Envelope, sent.Envelope);
        Assert.Equal(pending.Id, sent.Id);
        Assert.Equal(ProjectId, sent.ProjectId);
    }

    [Fact]
    public async Task Leaves_an_event_pending_when_the_broker_refuses_it()
    {
        // The whole point. A failed publish is a delay, not a loss.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher { Refuses = _ => true };
        outbox.Seed(Pending(1));

        await RunUntil(outbox, publisher, () => outbox.Events[0].AttemptCount >= 1);

        var stuck = Assert.Single(outbox.Events);
        Assert.False(stuck.IsPublished);
        Assert.Null(stuck.PublishedAt);
        Assert.Equal("The broker could not be reached.", stuck.LastError);
    }

    [Fact]
    public async Task Keeps_trying_an_event_the_broker_refused()
    {
        // A single attempt would make the outbox a log rather than a queue.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher { Refuses = _ => true };
        outbox.Seed(Pending(1));

        await RunUntil(outbox, publisher, () => publisher.Attempts.Count >= 2);

        Assert.False(outbox.Events[0].IsPublished);
        Assert.True(outbox.Events[0].AttemptCount >= 2);
    }

    [Fact]
    public async Task Delivers_an_event_once_the_broker_comes_back()
    {
        // The failure this story exists to fix, start to finish: an event raised
        // while the broker was down gets there on its own once it returns, with
        // the record of having struggled cleared.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher { Refuses = _ => true };
        outbox.Seed(Pending(1));

        await using var running = await StartAsync(outbox, publisher);

        await WaitUntil(() => outbox.Events[0].AttemptCount >= 1, "the first attempt has failed");

        // The broker returns.
        publisher.Refuses = _ => false;

        await WaitUntil(() => outbox.Events[0].IsPublished, "the event has been delivered");

        var delivered = Assert.Single(outbox.Events);
        Assert.True(delivered.AttemptCount >= 2);
        Assert.Null(delivered.LastError);
    }

    [Fact]
    public async Task Sends_events_oldest_first()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        // Seeded out of order, so passing cannot be an accident of insertion.
        outbox.Seed(Pending(3), Pending(1), Pending(2));

        await RunUntil(outbox, publisher, () => publisher.Attempts.Count == 3);

        Assert.Equal([1, 2, 3], publisher.Attempts.Select(e => e.SequenceNumber));
    }

    [Fact]
    public async Task Stops_the_batch_at_the_first_event_it_could_not_send()
    {
        // Rather than skipping past it. Events are ordered, and a project's
        // ProjectApproved arriving while the ProjectUpdated carrying the same
        // move is still stuck would show a consumer a state the project never
        // passed through.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher { Refuses = e => e.SequenceNumber == 2 };
        outbox.Seed(Pending(1), Pending(2), Pending(3));

        await RunUntil(outbox, publisher, () => outbox.Events[1].AttemptCount >= 1);

        // The third is never reached while the second is stuck.
        Assert.DoesNotContain(publisher.Attempts, e => e.SequenceNumber == 3);
        Assert.False(outbox.Events[2].IsPublished);
    }

    [Fact]
    public async Task Does_not_send_an_event_that_has_already_gone()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        outbox.Seed(Delivered(1), Pending(2));

        await RunUntil(outbox, publisher, () => publisher.Attempts.Count == 1);

        var sent = Assert.Single(publisher.Attempts);
        Assert.Equal(2, sent.SequenceNumber);
    }

    [Fact]
    public async Task Asks_for_no_more_than_the_configured_batch()
    {
        // Bounded so a long backlog is drained in chunks rather than read into
        // memory all at once.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        outbox.Seed([.. Enumerable.Range(1, 10).Select(i => Pending(i))]);

        await RunUntil(outbox, publisher, () => publisher.Attempts.Count >= 4, batchSize: 4);

        Assert.Equal(4, outbox.LastBatchSize);
    }

    [Fact]
    public async Task Drains_a_backlog_without_waiting_a_poll_interval_per_batch()
    {
        // A pass that filled its batch goes straight round again. With a poll
        // interval far longer than this test's patience, finishing at all is the
        // assertion.
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        outbox.Seed([.. Enumerable.Range(1, 9).Select(i => Pending(i))]);

        await RunUntil(
            outbox,
            publisher,
            () => outbox.Events.All(e => e.IsPublished),
            batchSize: 3,
            pollIntervalSeconds: 3600);

        Assert.Equal(9, publisher.Attempts.Count);
    }

    [Fact]
    public async Task Stops_cleanly_when_the_service_shuts_down()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeEventPublisher();
        outbox.Seed(Pending(1));

        var dispatcher = Dispatcher(outbox, publisher);
        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntil(() => outbox.Events[0].IsPublished, "the event has been delivered");

        // Returns rather than hanging or throwing on the way out.
        await dispatcher.StopAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------ harness ----

    /// <summary>
    /// Runs the dispatcher until <paramref name="until"/> holds, then stops it.
    /// </summary>
    private static async Task RunUntil(
        FakeOutboxRepository outbox,
        FakeEventPublisher publisher,
        Func<bool> until,
        int batchSize = 50,
        int pollIntervalSeconds = 1)
    {
        await using var _ = await StartAsync(outbox, publisher, batchSize, pollIntervalSeconds);

        await WaitUntil(until, "the dispatcher had done its work");
    }

    /// <summary>
    /// Starts a dispatcher and hands back something that stops it again,
    /// whatever the test does in between.
    /// </summary>
    private static async Task<RunningDispatcher> StartAsync(
        FakeOutboxRepository outbox,
        FakeEventPublisher publisher,
        int batchSize = 50,
        int pollIntervalSeconds = 1)
    {
        var dispatcher = Dispatcher(outbox, publisher, batchSize, pollIntervalSeconds);

        await dispatcher.StartAsync(CancellationToken.None);

        return new RunningDispatcher(dispatcher);
    }

    private static OutboxDispatcher Dispatcher(
        FakeOutboxRepository outbox,
        FakeEventPublisher publisher,
        int batchSize = 50,
        int pollIntervalSeconds = 1)
    {
        // The dispatcher resolves the outbox per pass, the way it does in the
        // service — it outlives any one scope, and the repositories are scoped.
        var services = new ServiceCollection()
            .AddScoped<BuildNexus.ProjectService.Data.IOutboxRepository>(_ => outbox)
            .BuildServiceProvider();

        return new OutboxDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions
            {
                BatchSize = batchSize,
                PollIntervalSeconds = pollIntervalSeconds
            }),
            NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>
    /// Waits for a condition the dispatcher brings about on its own thread.
    /// </summary>
    /// <remarks>
    /// Polled rather than slept on: a passing test finishes as soon as the work
    /// is done instead of always paying the worst case, and a broken one fails
    /// with the reason rather than hanging until the runner gives up.
    /// </remarks>
    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out after 10s waiting until {because}.");
    }

    private static OutboxEvent Pending(long sequenceNumber) => new()
    {
        SequenceNumber = sequenceNumber,
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        EventType = ProjectEventTypes.ProjectUpdated,
        // Opaque to the dispatcher, which carries the bytes rather than reading
        // them — the envelope's real shape is ProjectStatusEventTests' business.
        Envelope = "{\"eventType\":\"ProjectUpdated\",\"payload\":{}}",
        OccurredAt = new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc)
    };

    private static OutboxEvent Delivered(long sequenceNumber)
    {
        var outboxEvent = Pending(sequenceNumber);
        outboxEvent.PublishedAt = new DateTime(2026, 8, 31, 14, 5, 1, DateTimeKind.Utc);
        outboxEvent.AttemptCount = 1;

        return outboxEvent;
    }

    /// <summary>Stops the dispatcher when the test leaves its scope.</summary>
    private sealed class RunningDispatcher(OutboxDispatcher dispatcher) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await dispatcher.StopAsync(CancellationToken.None);
            dispatcher.Dispose();
        }
    }
}
