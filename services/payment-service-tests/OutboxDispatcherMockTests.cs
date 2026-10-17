using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using BuildNexus.PaymentService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The conversation between <see cref="OutboxDispatcher"/>, the outbox table and
/// the broker, checked with Moq mocks (AC-3).
/// </summary>
/// <remarks>
/// The older <see cref="OutboxDispatcherTests"/> use a fake table and a fake
/// broker and check the state they end up in. These check the calls instead: the
/// outbox says what is pending (a stub), the broker may refuse (a stub that
/// throws), and the test then verifies who was told what — an event is marked
/// published only after the broker took it, and marked failed only after it did
/// not.
/// <para>
/// The stubbed outbox hands over its events on the first call and an empty list
/// after that. Without that the same event would come back on every pass and the
/// call counts would depend on how long the test took. Each test waits for a
/// signal set by a mock callback rather than sleeping, then stops the dispatcher
/// before it verifies anything.
/// </para>
/// </remarks>
public class OutboxDispatcherMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b");

    private readonly Mock<IOutboxRepository> _outbox = new();
    private readonly Mock<IPaymentEventPublisher> _publisher = new();

    // Completes the first time the dispatcher reads the outbox.
    private readonly TaskCompletionSource _outboxRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static TheoryData<int> PendingCounts => new() { 0, 1, 3, 7 };

    [Fact]
    public async Task A_pending_event_is_sent_once_and_then_marked_published_once()
    {
        var pending = Pending(1);
        StubPending(pending);
        var marked = NotifyAfterMarkedPublished(times: 1);

        await RunUntil(marked);

        _publisher.Verify(p => p.PublishAsync(pending, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(
            o => o.MarkPublishedAsync(
                pending.Id,
                It.Is<DateTime>(at => at.Kind == DateTimeKind.Utc),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outbox.Verify(
            o => o.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_event_the_broker_refuses_is_marked_failed_and_never_marked_published()
    {
        // The whole point of the outbox: a refused publish is a delay, not a loss.
        var pending = Pending(1);
        StubPending(pending);
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The broker could not be reached."));
        var failed = NotifyWhenMarkedFailed();

        await RunUntil(failed);

        _outbox.Verify(
            o => o.MarkFailedAsync(pending.Id, "The broker could not be reached.", It.IsAny<CancellationToken>()),
            Times.Once);
        _outbox.Verify(
            o => o.MarkPublishedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("The broker could not be reached.", null, "The broker could not be reached.")]
    [InlineData("Local: Message timed out", "socket closed", "Local: Message timed out (socket closed)")]
    public async Task The_failure_recorded_includes_the_inner_message_when_there_is_one(
        string message,
        string? innerMessage,
        string expectedRecorded)
    {
        // A bare broker exception often says far less than the one it wraps.
        var pending = Pending(1);
        StubPending(pending);
        var thrown = innerMessage is null
            ? new InvalidOperationException(message)
            : new InvalidOperationException(message, new InvalidOperationException(innerMessage));
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);
        var failed = NotifyWhenMarkedFailed();

        await RunUntil(failed);

        _outbox.Verify(
            o => o.MarkFailedAsync(pending.Id, expectedRecorded, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_batch_stops_at_the_first_refused_event_and_never_reaches_the_one_behind_it()
    {
        // Events are ordered: sending the third while the second is stuck would
        // show a consumer a balance that went up.
        var first = Pending(1);
        var second = Pending(2);
        var third = Pending(3);
        StubPending(first, second, third);
        _publisher
            .Setup(p => p.PublishAsync(second, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The broker could not be reached."));
        var failed = NotifyWhenMarkedFailed();

        await RunUntil(failed);

        _publisher.Verify(p => p.PublishAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(
            o => o.MarkPublishedAsync(first.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outbox.Verify(
            o => o.MarkFailedAsync(second.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(p => p.PublishAsync(third, It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(
            o => o.MarkPublishedAsync(third.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(PendingCounts))]
    public async Task Every_pending_event_is_sent_once_and_marked_once_and_an_empty_outbox_sends_nothing(int count)
    {
        var pending = Enumerable.Range(1, count).Select(i => Pending(i)).ToArray();
        StubPending(pending);
        var finished = count == 0 ? _outboxRead.Task : NotifyAfterMarkedPublished(times: count);

        await RunUntil(finished);

        _publisher.Verify(
            p => p.PublishAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(count));
        _outbox.Verify(
            o => o.MarkPublishedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(count));

        foreach (var sent in pending)
        {
            _publisher.Verify(p => p.PublishAsync(sent, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(50)]
    [InlineData(500)]
    public async Task The_outbox_is_asked_for_exactly_the_configured_batch_size(int batchSize)
    {
        StubPending();

        await RunUntil(_outboxRead.Task, batchSize);

        _outbox.Verify(
            o => o.ListPendingAsync(batchSize, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task A_database_failure_on_one_pass_does_not_stop_the_dispatcher()
    {
        // The first read throws; the next pass (after the one-second poll
        // interval) finds the event and sends it. A dispatcher that ended on the
        // first bad minute would leave every later event unsent.
        var pending = Pending(1);
        var reads = 0;
        _outbox
            .Setup(o => o.ListPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref reads) switch
            {
                1 => throw new InvalidOperationException("payment-db is unreachable."),
                2 => Task.FromResult<IReadOnlyList<OutboxEvent>>([pending]),
                _ => Task.FromResult<IReadOnlyList<OutboxEvent>>([])
            });
        var marked = NotifyAfterMarkedPublished(times: 1);

        await RunUntil(marked);

        _publisher.Verify(p => p.PublishAsync(pending, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(
            o => o.MarkPublishedAsync(pending.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------ harness ----

    /// <summary>
    /// The outbox hands over <paramref name="events"/> on its first read and an
    /// empty list on every read after.
    /// </summary>
    private void StubPending(params OutboxEvent[] events)
    {
        var reads = 0;

        _outbox
            .Setup(o => o.ListPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => _outboxRead.TrySetResult())
            .ReturnsAsync(() => Interlocked.Increment(ref reads) == 1
                ? events
                : Array.Empty<OutboxEvent>());
    }

    /// <summary>Completes once <c>MarkPublishedAsync</c> has been called this many times.</summary>
    private Task NotifyAfterMarkedPublished(int times)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        _outbox
            .Setup(o => o.MarkPublishedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (Interlocked.Increment(ref calls) == times)
                {
                    done.TrySetResult();
                }
            });

        return done.Task;
    }

    /// <summary>Completes the first time <c>MarkFailedAsync</c> is called.</summary>
    private Task NotifyWhenMarkedFailed()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _outbox
            .Setup(o => o.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => done.TrySetResult());

        return done.Task;
    }

    /// <summary>
    /// Starts the real dispatcher over the mocks, waits for the signal (up to ten
    /// seconds, so a broken run fails instead of hanging), and stops it again so
    /// the verifications see a finished run.
    /// </summary>
    private async Task RunUntil(Task signal, int batchSize = 50)
    {
        // The dispatcher resolves the outbox per pass, the way it does in the
        // service — it outlives any one scope, and the repositories are scoped.
        var services = new ServiceCollection()
            .AddScoped<IOutboxRepository>(_ => _outbox.Object)
            .BuildServiceProvider();

        var dispatcher = new OutboxDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            _publisher.Object,
            Options.Create(new OutboxOptions { BatchSize = batchSize, PollIntervalSeconds = 1 }),
            NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            await signal.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            dispatcher.Dispose();
        }
    }

    private static OutboxEvent Pending(long sequenceNumber) => new()
    {
        SequenceNumber = sequenceNumber,
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        InvoiceId = Guid.NewGuid(),
        EventType = PaymentEventTypes.PaymentReceived,
        // Opaque to the dispatcher, which carries the bytes rather than reading them.
        Envelope = "{\"eventType\":\"PaymentReceived\",\"payload\":{}}",
        OccurredAt = new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc)
    };
}
