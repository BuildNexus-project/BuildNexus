using BuildNexus.DesignService.Configuration;
using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Models;
using Microsoft.Extensions.Options;

namespace BuildNexus.DesignService.Messaging;

/// <summary>
/// Drains the outbox onto Kafka: the half of a reliable publish that happens
/// after the transaction has committed.
/// </summary>
/// <remarks>
/// The endpoints record what happened and this sends it, which is what lets a
/// state change succeed while the broker is down. An event is not lost by a
/// failed attempt, only delayed — it stays pending, and the next pass tries
/// again.
/// <para>
/// Delivery is at-least-once, not exactly-once. If the broker takes a message
/// and the process dies before the row can be marked, the same envelope is sent
/// again on the next pass — the same bytes, so the same <c>eventId</c>, which
/// is precisely what a consumer needs to recognise the duplicate. Losing an
/// event is the failure worth engineering against; sending one twice is one a
/// consumer can absorb.
/// </para>
/// </remarks>
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDesignEventPublisher _publisher;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly OutboxOptions _options;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IDesignEventPublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox dispatcher started; polling every {PollIntervalSeconds}s in batches of {BatchSize}.",
            _options.PollIntervalSeconds,
            _options.BatchSize);

        var pollInterval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;

            try
            {
                sent = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. Whatever was not sent is still on the outbox.
                break;
            }
            catch (Exception ex)
            {
                // Reaching the database failed, or something else outside the
                // per-event handling below. Logged and slept off rather than
                // allowed to end the loop: a dispatcher that stopped on the
                // first bad minute would leave every later event unsent with
                // nothing running to notice.
                _logger.LogError(ex, "Outbox dispatch pass failed. Retrying after the poll interval.");
            }

            // A pass that filled its batch probably has more waiting, so it goes
            // straight round again — a backlog drains at the broker's pace
            // instead of one batch per interval. Only an idle outbox waits.
            if (sent >= _options.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox dispatcher stopped.");
    }

    /// <summary>
    /// Sends one batch of pending events, oldest first, and returns how many
    /// the broker took.
    /// </summary>
    /// <remarks>
    /// Stops at the first failure rather than skipping past it, the same
    /// reasoning <c>project-service</c>'s identical dispatcher documents: events
    /// are ordered, and letting one wait behind a genuinely broken broker is a
    /// better failure than publishing out of order to keep the queue moving.
    /// </remarks>
    private async Task<int> DispatchBatchAsync(CancellationToken stoppingToken)
    {
        // A scope per pass: the repositories are scoped, and this runs for the
        // life of the process.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var pending = await outbox.ListPendingAsync(_options.BatchSize, stoppingToken);
        var sent = 0;

        foreach (var outboxEvent in pending)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!await TryDispatchAsync(outbox, outboxEvent, stoppingToken))
            {
                break;
            }

            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Sends one event and records how it went. <c>false</c> if the broker did
    /// not take it, in which case it is still pending.
    /// </summary>
    private async Task<bool> TryDispatchAsync(
        IOutboxRepository outbox,
        OutboxEvent outboxEvent,
        CancellationToken stoppingToken)
    {
        try
        {
            await _publisher.PublishAsync(outboxEvent, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown cut the publish short. Nothing to record — the event is
            // pending, which is exactly what it is.
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not publish {EventType} {EventId} for document {DocumentId}. It stays on the outbox.",
                outboxEvent.EventType,
                outboxEvent.Id,
                outboxEvent.DocumentId);

            // Deliberately not the stopping token: a failure recorded is what
            // makes a stuck event visible, and dropping that during shutdown
            // would hide the very thing somebody needs to see.
            await outbox.MarkFailedAsync(outboxEvent.Id, Describe(ex), CancellationToken.None);

            return false;
        }

        // Also not the stopping token. The broker has taken the message; if this
        // write is skipped the event is sent a second time on the next start,
        // and recording a delivery we actually made is worth the moment it costs
        // on the way out.
        await outbox.MarkPublishedAsync(outboxEvent.Id, DateTime.UtcNow, CancellationToken.None);

        return true;
    }

    /// <summary>
    /// The failure as one line, innermost message included — a bare
    /// <c>KafkaException</c> message often says far less than the exception it
    /// wraps.
    /// </summary>
    private static string Describe(Exception ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";
}
