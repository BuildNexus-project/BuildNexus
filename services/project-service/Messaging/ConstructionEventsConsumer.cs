using System.Text.Json;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Reads <c>construction-events</c> and moves a project through the build half of
/// its lifecycle: <c>ConstructionStarted</c> takes it to
/// <see cref="ProjectStatus.Construction"/>, <c>ConstructionCompleted</c> to
/// <see cref="ProjectStatus.Completed"/> (US-14).
/// </summary>
/// <remarks>
/// The first consumer in this service, which until now only published. It exists
/// because US-14's first Acceptance Criterion is about a project's <em>status</em>,
/// and that column belongs here — the Construction Service owns the build phase and
/// announces it, this service owns the lifecycle and reacts. Neither reaches into
/// the other's database.
/// <para>
/// The move goes through the same <see cref="IProjectRepository.UpdateStatusAsync"/>
/// the HTTP endpoint uses, so a Kafka-driven transition is recorded in the status
/// history and announces its own <c>ProjectUpdated</c> exactly as a human-driven one
/// does. Nothing about the lifecycle is special-cased for having arrived over a
/// topic.
/// </para>
/// <para>
/// Resilience follows the pattern the Construction Service's consumers set:
/// <list type="bullet">
/// <item>A message that cannot be parsed is logged and its offset committed past —
/// it will never parse, and wedging the partition on it would stop every event
/// behind it.</item>
/// <item>A message that parses but describes a move this service will not make — an
/// unknown project, a cancelled one, a status the transition table forbids — is
/// logged and committed past too. It is a business answer, not a failure, and
/// retrying it forever would never change it.</item>
/// <item>A message whose handling fails — the database is unreachable — is
/// <em>not</em> committed. The next poll re-reads it and tries again.</item>
/// <item>A redelivery of a move already made is absorbed: the project is already in
/// the target status, so there is nothing to do and it is not an error.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ConstructionEventsConsumer : BackgroundService
{
    private const string Topic = "construction-events";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The group this consumer reads under: the configured id with the topic appended.
    /// </summary>
    /// <remarks>
    /// Suffixed rather than bare even though this is currently the only consumer in this
    /// service. Every member of a Kafka consumer group is expected to subscribe to the
    /// same topics, so the moment a second consumer here used the bare id the two would
    /// revoke each other's partitions on every rebalance and take turns being assigned
    /// nothing. Naming the group per topic from the start means that trap is never set —
    /// and it costs nothing now, while no offsets are committed under it anywhere.
    /// </remarks>
    private string GroupId => $"{_options.ConsumerGroupId}-construction-events";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ConstructionEventsConsumer> _logger;

    public ConstructionEventsConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ConstructionEventsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the request thread: BackgroundService runs ExecuteAsync inline during
        // startup until the first await, and the consume loop is a long-running one.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = GroupId,
            // Commit explicitly, only after a message is handled — so a crash
            // mid-handling re-delivers rather than skips.
            EnableAutoCommit = false,
            // A brand-new group reads the topic from the start, so transitions
            // published before this service first ran are not missed.
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topic);

        _logger.LogInformation(
            "Consuming {Topic} as group {GroupId} from {BootstrapServers}.",
            Topic, GroupId, _options.BootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;

                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed. Retrying after {Delay}.", RetryDelay);
                    await DelayAsync(stoppingToken);
                    continue;
                }

                await ProcessAsync(consumer, result, stoppingToken);
            }
        }
        finally
        {
            // Leave the group cleanly so a restart does not wait out the session
            // timeout before its partitions are reassigned.
            consumer.Close();
            _logger.LogInformation("Stopped consuming {Topic}.", Topic);
        }
    }

    private async Task ProcessAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        CancellationToken stoppingToken)
    {
        try
        {
            await HandleAsync(result.Message.Value, stoppingToken);
            consumer.Commit(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-handling. Not committed — it re-delivers on the next
            // start, and a redelivered move is absorbed.
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Skipping a message on {TopicPartitionOffset} that could not be parsed as a BuildNexus event.",
                result.TopicPartitionOffset);

            // Commit past it: a malformed message will not parse on a retry, and
            // holding the partition on it would stop everything behind it.
            consumer.Commit(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Handling the message on {TopicPartitionOffset} failed. Leaving the offset uncommitted to retry.",
                result.TopicPartitionOffset);

            await DelayAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Processes one message body: a construction transition becomes a project
    /// status change, any other event type on the topic is a no-op.
    /// </summary>
    /// <remarks>
    /// The unit the tests drive directly. Its exception contract is what
    /// <see cref="ProcessAsync"/> maps to a commit decision: a
    /// <see cref="JsonException"/> is a message that will never parse, so the caller
    /// commits past it; anything else is treated as transient, so the caller leaves
    /// the offset uncommitted and the message is retried.
    /// </remarks>
    public async Task HandleAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("The message body was empty.");
        }

        var envelope = JsonSerializer.Deserialize<IncomingEvent>(value, IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The message body deserialised to null.");

        // Which transition this event asks for, if any. Decided before the payload is
        // read, so an event type this service does not handle costs nothing.
        var target = TargetStatusFor(envelope.EventType);

        if (target is null)
        {
            return;
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"A {envelope.EventType} event carried no payload object.");
        }

        var (projectId, actingUserId) = ReadActor(envelope);

        if (projectId == Guid.Empty || actingUserId == Guid.Empty)
        {
            // Without the project there is nothing to move; without the actor the
            // status history would have no author, and inventing one would put a
            // false name in an audit trail.
            throw new JsonException($"The {envelope.EventType} payload was missing its projectId or actor.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

        var project = await projects.GetByIdAsync(projectId);

        if (project is null)
        {
            // This service owns projects, so a construction event about one it has
            // never heard of cannot be a race — it is a bogus or very stale message.
            // Retrying would never find it.
            _logger.LogWarning(
                "Ignoring {EventType} {EventId}: no project {ProjectId} exists in this service.",
                envelope.EventType, envelope.EventId, projectId);

            return;
        }

        if (project.Status == target.Value)
        {
            // Kafka delivers at least once, and the dispatcher re-sends anything it
            // could not confirm. A project already in the target status has already
            // had this event applied, which is a success, not a conflict.
            _logger.LogInformation(
                "Project {ProjectId} is already {Status}; {EventType} {EventId} absorbed.",
                projectId, target.Value, envelope.EventType, envelope.EventId);

            return;
        }

        if (!ProjectStatusTransitions.IsAllowed(project.Status, target.Value))
        {
            // A cancelled project, or one whose ConstructionStarted never arrived so
            // it is still DesignApproved when the Completed event lands. Either way
            // the lifecycle rule is the same rule the HTTP endpoint enforces, and
            // this service will not bend it because the request came over a topic.
            _logger.LogWarning(
                "Ignoring {EventType} {EventId}: project {ProjectId} is {Current}, which cannot move to {Target}.",
                envelope.EventType, envelope.EventId, projectId, project.Status, target.Value);

            return;
        }

        var change = new ProjectStatusChange
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            FromStatus = project.Status,
            ToStatus = target.Value,
            // The Project Manager named in the event, never a placeholder: the
            // Construction Service took this id from the token of whoever pressed the
            // button, and it is the only honest author for this move.
            ChangedByUserId = actingUserId,
            ChangedByRole = PlatformRoles.ProjectManager,
            // When the build actually moved, not when this message happened to be
            // read — a consumer that was down for an hour must not backdate history
            // to now.
            ChangedAt = envelope.OccurredAt.UtcDateTime,
            Note = $"Recorded from the Construction Service's {envelope.EventType} event."
        };

        // Built from the change rather than the project, which still holds the old
        // status at this point — the same way the HTTP endpoint builds them.
        var raised = ProjectEvents.ForStatusChange(project, change);

        var moved = await projects.UpdateStatusAsync(change, change.ChangedAt, raised);

        if (!moved)
        {
            // The conditional update found the project no longer holding the status
            // it was read at: somebody moved it in between. Not committing means the
            // message is re-read, and the re-read sees the new status — either as
            // "already there" or as a move the table forbids.
            throw new InvalidOperationException(
                $"Project {projectId} moved underneath {envelope.EventType} {envelope.EventId}; retrying.");
        }

        _logger.LogInformation(
            "Moved project {ProjectId} from {From} to {To} on {EventType} {EventId}.",
            projectId, change.FromStatus, change.ToStatus, envelope.EventType, envelope.EventId);
    }

    /// <summary>
    /// The lifecycle status an event type asks for, or <c>null</c> when this service
    /// does not react to it.
    /// </summary>
    private static ProjectStatus? TargetStatusFor(string eventType) => eventType switch
    {
        ConstructionEventTypes.ConstructionStarted => ProjectStatus.Construction,
        ConstructionEventTypes.ConstructionCompleted => ProjectStatus.Completed,
        _ => null
    };

    /// <summary>
    /// The project and the Project Manager the event names. The actor's field is
    /// named for the transition it describes, so each payload is read as its own type.
    /// </summary>
    private static (Guid ProjectId, Guid ActingUserId) ReadActor(IncomingEvent envelope)
    {
        if (envelope.EventType == ConstructionEventTypes.ConstructionStarted)
        {
            var started = envelope.Payload.Deserialize<ConstructionStartedPayload>(
                IncomingEvent.SerializerOptions)
                ?? throw new JsonException("The ConstructionStarted payload deserialised to null.");

            return (started.ProjectId, started.StartedBy);
        }

        var completed = envelope.Payload.Deserialize<ConstructionCompletedPayload>(
            IncomingEvent.SerializerOptions)
            ?? throw new JsonException("The ConstructionCompleted payload deserialised to null.");

        return (completed.ProjectId, completed.CompletedBy);
    }

    private async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetryDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
