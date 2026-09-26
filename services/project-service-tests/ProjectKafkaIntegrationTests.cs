using System.Text.Json;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Sdk;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The Project Service's two Kafka paths against a real broker and a real MySQL,
/// with nothing stubbed between them (US-30).
/// </summary>
/// <remarks>
/// Every other class in this project that touches Kafka does so over a fake, which
/// is right for the decisions they cover — but a fake cannot show that a message
/// actually crosses the wire, that the envelope survives the round trip through a
/// MySQL column and a broker unchanged, or that a consumer's commit and rebalance
/// behave. These two tests exist for exactly that:
/// <list type="bullet">
/// <item><b>Publish:</b> a project and its <c>ProjectCreated</c> are written to
/// MySQL, the real <see cref="OutboxDispatcher"/> and
/// <see cref="KafkaProjectEventPublisher"/> send it, and an independent consumer
/// reads it back off <c>project-events</c> — while the outbox row is marked
/// published in the same database.</item>
/// <item><b>Consume:</b> a <c>ConstructionStarted</c> is produced onto
/// <c>construction-events</c>, the real <see cref="ConstructionEventsConsumer"/>
/// reads it, and the project's status, history and outbox all change in MySQL.</item>
/// </list>
/// <para>
/// Needs <c>project-db</c> and <c>kafka</c> running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait project-db kafka</c>. The broker
/// address comes from <c>Kafka__BootstrapServers</c>, the same name the service
/// itself reads, and falls back to the compose broker's host listener. CI starts
/// both as its own step.
/// </para>
/// <para>
/// The topic names are fixed by the service, so on a machine whose full stack is
/// running these tests publish onto the topics that stack's own consumers are
/// reading. Point <c>Kafka__BootstrapServers</c> at a disposable broker to avoid
/// that; on CI the broker is new for the run.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ProjectDatabaseCollection.Name)]
public class ProjectKafkaIntegrationTests
{
    /// <summary>
    /// How long a step may take to show up before the test gives up. Generous on
    /// purpose: a fresh broker creates a topic and elects a group coordinator on
    /// first use, and a test that fails on a slow runner proves nothing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private const string ConstructionEventsTopic = "construction-events";

    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");

    /// <summary>Whole seconds, because that is all a MySQL <c>DATETIME</c> keeps.</summary>
    private static readonly DateTime CreatedAt = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly ProjectDatabaseFixture _fixture;

    public ProjectKafkaIntegrationTests(ProjectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------ publish: MySQL -> Kafka ----

    [Fact]
    public async Task An_event_enqueued_with_a_project_is_dispatched_to_project_events_and_marked_published()
    {
        var project = NewProject(ProjectStatus.Pending);
        var raised = ProjectEvents.Created(project);

        // The real INSERT: project, opening history row and the event in one
        // transaction, exactly as the create endpoint writes them.
        await _fixture.Repository.InsertAsync(
            project,
            ProjectStatusChange.ForCreation(project, PlatformRoles.Client),
            [raised]);

        using (var publisher = new KafkaProjectEventPublisher(
            Options.Create(BrokerOptions()),
            NullLogger<KafkaProjectEventPublisher>.Instance))
        {
            var dispatcher = Dispatcher(publisher, project.Id);
            await dispatcher.StartAsync(CancellationToken.None);

            try
            {
                await WaitUntilAsync(
                    async () => (await _fixture.Outbox.ListForProjectAsync(project.Id)).All(e => e.IsPublished),
                    "the dispatcher to mark the event published");
            }
            finally
            {
                await dispatcher.StopAsync(CancellationToken.None);
            }
        }

        // The database side of "delivered": stamped, counted, no error left over.
        // At least one attempt rather than exactly one — delivery is at-least-once,
        // and a second dispatcher (another replica, or the stack running beside this
        // test) may have sent and counted it too.
        var row = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));
        Assert.True(row.IsPublished);
        Assert.True(row.AttemptCount >= 1);
        Assert.Null(row.LastError);

        // The broker side, read by a consumer that shares nothing with the service:
        // a fresh group, from the start of the topic, matching on the message key.
        var message = ReadFromStart(
            KafkaProjectEventPublisher.Topic,
            m => m.Message.Key == project.Id.ToString());

        // Byte for byte what was written to MySQL — through the column, the
        // dispatcher and the broker — so a retry can never mint a new eventId.
        Assert.Equal(raised.Envelope, message.Message.Value);

        // And it is the agreed envelope, not merely equal to whatever was stored.
        using var envelope = JsonDocument.Parse(message.Message.Value);
        var root = envelope.RootElement;
        Assert.Equal(ProjectEventTypes.ProjectCreated, root.GetProperty("eventType").GetString());
        Assert.Equal(raised.Id, root.GetProperty("eventId").GetGuid());
        Assert.True(root.TryGetProperty("occurredAt", out _));
        Assert.Equal(project.Id, root.GetProperty("payload").GetProperty("projectId").GetGuid());
    }

    // ------------------------------------------ consume: Kafka -> MySQL ----

    [Fact]
    public async Task A_ConstructionStarted_event_on_construction_events_moves_the_project_in_MySQL()
    {
        var project = NewProject(ProjectStatus.DesignApproved);

        await _fixture.Repository.InsertAsync(
            project,
            ProjectStatusChange.ForCreation(project, PlatformRoles.Client),
            []);

        var startedBy = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);

        // Produced first, so the topic exists by the time the consumer subscribes
        // on a broker that has never seen it. The Construction Service is not
        // referenced from here — this is its wire format, which is the contract
        // this service actually depends on.
        await ProduceAsync(
            ConstructionEventsTopic,
            project.Id.ToString(),
            ConstructionStartedEnvelope(project.Id, startedBy, occurredAt));

        var consumer = ConstructionConsumer();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                async () => (await _fixture.Repository.GetByIdAsync(project.Id))?.Status == ProjectStatus.Construction,
                "the consumer to move the project to Construction");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        // Everything below is read back through the real repository, so it is
        // what MySQL holds and not what the consumer believes it wrote.
        var moved = await _fixture.Repository.GetByIdAsync(project.Id);
        Assert.Equal(ProjectStatus.Construction, moved!.Status);

        var change = Assert.Single(
            await _fixture.Repository.GetStatusHistoryAsync(project.Id),
            h => h.ToStatus == ProjectStatus.Construction);
        Assert.Equal(ProjectStatus.DesignApproved, change.FromStatus);
        Assert.Equal(startedBy, change.ChangedByUserId);
        Assert.Equal(PlatformRoles.ProjectManager, change.ChangedByRole);
        Assert.Equal(occurredAt.UtcDateTime, change.ChangedAt);

        // A Kafka-driven move announces itself like any other: the ProjectUpdated
        // was enqueued in the same transaction as the status.
        var announced = await _fixture.Outbox.ListForProjectAsync(project.Id);
        Assert.Contains(announced, e => e.EventType == ProjectEventTypes.ProjectUpdated);
    }

    // ------------------------------------------------------------ helpers ----

    /// <summary>
    /// The broker to talk to: <c>Kafka__BootstrapServers</c> when set, otherwise the
    /// compose broker's host listener.
    /// </summary>
    private static string BootstrapServers =>
        Environment.GetEnvironmentVariable("Kafka__BootstrapServers") is { Length: > 0 } configured
            ? configured
            : "localhost:29092";

    private static KafkaOptions BrokerOptions() => new()
    {
        BootstrapServers = BootstrapServers,
        // Short, so a broker that is not there fails the test in seconds rather
        // than after librdkafka's five-minute default.
        MessageTimeoutMs = 10_000,
        // A group of its own per test, so a second run never inherits the offset
        // a first one committed and skips the message it is here to see.
        ConsumerGroupId = $"project-service-integration-{Guid.NewGuid():N}"
    };

    private static Project NewProject(ProjectStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = ClientId,
        // Named so the fixture's cleanup finds it when the collection is done.
        Name = $"{ProjectDatabaseFixture.TestProjectPrefix}{Guid.NewGuid()}",
        Location = "Galle",
        LandSizePerches = 25.5m,
        Budget = 18_500_000m,
        Floors = 2,
        Bedrooms = 4,
        Bathrooms = 3,
        GarageSpaces = 2,
        OtherRequirements = null,
        Status = status,
        CreatedAt = CreatedAt,
        UpdatedAt = CreatedAt
    };

    /// <summary>
    /// The real dispatcher, over the real outbox — narrowed to one project.
    /// </summary>
    /// <remarks>
    /// Left alone it would drain every pending row in the table, and the table is
    /// shared: other tests leave pending events behind until the collection ends,
    /// and a developer's own database may hold real ones. Sending those to whatever
    /// broker this run points at, and marking them delivered, would be a side effect
    /// no test should have.
    /// </remarks>
    private OutboxDispatcher Dispatcher(KafkaProjectEventPublisher publisher, Guid projectId)
    {
        var provider = new ServiceCollection()
            .AddScoped<IOutboxRepository>(_ => new SingleProjectOutbox(_fixture.Outbox, projectId))
            .BuildServiceProvider();

        return new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions { PollIntervalSeconds = 1, BatchSize = 500 }),
            NullLogger<OutboxDispatcher>.Instance);
    }

    private ConstructionEventsConsumer ConstructionConsumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IProjectRepository>(_ => _fixture.Repository)
            .BuildServiceProvider();

        return new ConstructionEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(BrokerOptions()),
            NullLogger<ConstructionEventsConsumer>.Instance);
    }

    /// <summary>
    /// A <c>ConstructionStarted</c> as the Construction Service puts it on the wire:
    /// the agreed envelope around its full payload, not just the fields this service
    /// happens to read.
    /// </summary>
    private static string ConstructionStartedEnvelope(Guid projectId, Guid startedBy, DateTimeOffset occurredAt) =>
        JsonSerializer.Serialize(new
        {
            eventType = ConstructionEventTypes.ConstructionStarted,
            eventId = Guid.NewGuid(),
            occurredAt,
            payload = new
            {
                projectId,
                startedAt = occurredAt,
                milestoneCount = 7,
                startedBy
            }
        });

    private static async Task ProduceAsync(string topic, string key, string value)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            Acks = Acks.All,
            MessageTimeoutMs = 10_000
        };

        using var producer = new ProducerBuilder<string, string>(config).Build();

        await producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = value });
    }

    /// <summary>
    /// Reads a topic from its first message, under a consumer group nobody else
    /// uses, until <paramref name="match"/> accepts a message.
    /// </summary>
    private static ConsumeResult<string, string> ReadFromStart(
        string topic,
        Func<ConsumeResult<string, string>, bool> match)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = $"project-service-integration-reader-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        var deadline = DateTime.UtcNow + Patience;
        string? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));

                if (result is not null && match(result))
                {
                    return result;
                }
            }
            catch (ConsumeException ex)
            {
                // Usually the topic not being known yet. Kept for the failure
                // message rather than thrown, so a slow broker is waited out.
                lastError = ex.Error.Reason;
            }
        }

        throw new XunitException(
            $"No matching message appeared on '{topic}' within {Patience.TotalSeconds:0}s." +
            (lastError is null ? string.Empty : $" Last consume error: {lastError}"));
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. The consumer and the
    /// dispatcher both work on their own threads, so the only honest way to wait
    /// for either is to look for what they leave in the database.
    /// </summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string waitingFor)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new XunitException($"Timed out after {Patience.TotalSeconds:0}s waiting for {waitingFor}.");
    }

    /// <summary>
    /// The real outbox with its pending list cut down to one project's events.
    /// Everything else — marking published, marking failed — goes straight through
    /// to MySQL.
    /// </summary>
    private sealed class SingleProjectOutbox : IOutboxRepository
    {
        private readonly IOutboxRepository _inner;
        private readonly Guid _projectId;

        public SingleProjectOutbox(IOutboxRepository inner, Guid projectId)
        {
            _inner = inner;
            _projectId = projectId;
        }

        public async Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            (await _inner.ListPendingAsync(batchSize, cancellationToken))
                .Where(e => e.ProjectId == _projectId)
                .ToList();

        public Task MarkPublishedAsync(
            Guid eventId,
            DateTime publishedAtUtc,
            CancellationToken cancellationToken = default) =>
            _inner.MarkPublishedAsync(eventId, publishedAtUtc, cancellationToken);

        public Task MarkFailedAsync(
            Guid eventId,
            string error,
            CancellationToken cancellationToken = default) =>
            _inner.MarkFailedAsync(eventId, error, cancellationToken);

        public Task<IReadOnlyList<OutboxEvent>> ListForProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            _inner.ListForProjectAsync(projectId, cancellationToken);
    }
}
