using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using BuildNexus.ConstructionService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Sdk;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The Construction Service's two Kafka paths against a real broker and a real MySQL,
/// with nothing stubbed between them (US-30).
/// </summary>
/// <remarks>
/// Every other class in this project that touches Kafka does so over a fake, which is
/// right for the decisions they cover — but a fake cannot show that a message actually
/// crosses the wire, that the envelope survives a MySQL column and a broker unchanged,
/// or that a consumer's commit and rebalance behave. These two tests exist for exactly
/// that:
/// <list type="bullet">
/// <item><b>Consume:</b> a <c>DesignApproved</c> is produced onto <c>design-events</c>
/// in the Design Service's wire format, the real <see cref="DesignEventsConsumer"/> reads
/// it, and the milestone-setup placeholder that opens the start gate appears in MySQL.
/// This is the cross-service link: what the Design Service publishes is what this
/// service acts on.</item>
/// <item><b>Publish:</b> a project's build is started through the real
/// <see cref="ConstructionPhaseRepository"/>, which enqueues <c>ConstructionStarted</c>
/// in its own transaction; the real <see cref="OutboxDispatcher"/> and
/// <see cref="KafkaConstructionEventPublisher"/> send it, and an independent consumer
/// reads it back off <c>construction-events</c> — while the outbox row is marked
/// published in the same database.</item>
/// </list>
/// <para>
/// The service's other two consumers, <c>project-events</c> and <c>payment-events</c>,
/// share the consume loop this test drives and are not repeated here.
/// </para>
/// <para>
/// Needs <c>construction-db</c> and <c>kafka</c> running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait construction-db kafka</c>. The broker
/// address comes from <c>Kafka__BootstrapServers</c>, the same name the service itself
/// reads, and falls back to the compose broker's host listener. CI starts both as its
/// own step.
/// </para>
/// <para>
/// The topic names are fixed by the service, so on a machine whose full stack is running
/// these tests publish onto the topics that stack's own consumers are reading. Point
/// <c>Kafka__BootstrapServers</c> at a disposable broker to avoid that; on CI the broker
/// is new for the run.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class ConstructionKafkaIntegrationTests
{
    /// <summary>
    /// How long a step may take to show up before the test gives up. Generous on
    /// purpose: a fresh broker creates a topic and elects a group coordinator on
    /// first use, and a test that fails on a slow runner proves nothing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private const string DesignEventsTopic = "design-events";

    /// <summary>The Project Manager who starts the build. Travels onto the event.</summary>
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private readonly ConstructionDatabaseFixture _fixture;

    public ConstructionKafkaIntegrationTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------ consume: Kafka -> MySQL ----

    [Fact]
    public async Task A_DesignApproved_event_on_design_events_creates_the_milestone_setup_placeholder_in_MySQL()
    {
        var projectId = NewProjectId();
        var documentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var approvedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

        // Produced first, so the topic exists by the time the consumer subscribes
        // on a broker that has never seen it. The Design Service is not referenced
        // from here — this is its wire format, which is the contract this service
        // actually depends on. Keyed by document id, as the Design Service keys it.
        await ProduceAsync(
            DesignEventsTopic,
            documentId.ToString(),
            DesignApprovedEnvelope(eventId, projectId, documentId, approvedAt));

        var consumer = DesignConsumer();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                async () => (await _fixture.Repository.ListAsync()).Any(s => s.ProjectId == projectId),
                "the consumer to create the milestone-setup placeholder");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        // Read back through the real repository, so this is what MySQL holds and
        // not what the consumer believes it wrote.
        var setup = Assert.Single(
            await _fixture.Repository.ListAsync(),
            s => s.ProjectId == projectId);
        Assert.Equal(documentId, setup.SourceDocumentId);
        // The event's own id is what lets a redelivery be recognised and absorbed.
        Assert.Equal(eventId, setup.SourceEventId);
        Assert.Equal(approvedAt.UtcDateTime, setup.ApprovedAtUtc);
    }

    // ------------------------------------------ publish: MySQL -> Kafka ----

    [Fact]
    public async Task Starting_a_build_enqueues_ConstructionStarted_which_is_dispatched_to_construction_events_and_marked_published()
    {
        var projectId = NewProjectId();

        // The two start preconditions, planted through the same real repositories the
        // service uses: a design signed off, and a plan with something in it.
        await _fixture.PlantApprovedDesignAsync(projectId);
        await _fixture.MilestoneRepository.CreateAsync(projectId, "Foundation");

        // The real transition. Its event is enqueued inside its own transaction —
        // there is no callback or argument to hand one in — so the stored row is the
        // only place to read what was raised.
        var started = await _fixture.ConstructionPhaseRepository.StartAsync(projectId, ActingPm);
        Assert.Equal(ConstructionTransitionOutcome.Succeeded, started.Outcome);

        var stored = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
        Assert.Equal(ConstructionEventTypes.ConstructionStarted, stored.EventType);
        Assert.False(stored.IsPublished);

        using (var publisher = new KafkaConstructionEventPublisher(
            Options.Create(BrokerOptions()),
            NullLogger<KafkaConstructionEventPublisher>.Instance))
        {
            var dispatcher = Dispatcher(publisher, projectId);
            await dispatcher.StartAsync(CancellationToken.None);

            try
            {
                await WaitUntilAsync(
                    async () => (await _fixture.OutboxRepository.ListForProjectAsync(projectId)).All(e => e.IsPublished),
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
        var row = Assert.Single(await _fixture.OutboxRepository.ListForProjectAsync(projectId));
        Assert.True(row.IsPublished);
        Assert.True(row.AttemptCount >= 1);
        Assert.Null(row.LastError);

        // The broker side, read by a consumer that shares nothing with the service:
        // a fresh group, from the start of the topic, matching on the message key.
        var message = ReadFromStart(
            KafkaConstructionEventPublisher.Topic,
            m => m.Message.Key == projectId.ToString());

        // Byte for byte what was stored in MySQL before dispatch — through the
        // column, the dispatcher and the broker — so a retry can never mint a new
        // eventId.
        Assert.Equal(stored.Envelope, message.Message.Value);

        // And it is the agreed envelope, with the fields the Project and Payment
        // Services read, not merely equal to whatever was stored.
        using var envelope = JsonDocument.Parse(message.Message.Value);
        var root = envelope.RootElement;
        Assert.Equal(ConstructionEventTypes.ConstructionStarted, root.GetProperty("eventType").GetString());
        Assert.Equal(stored.Id, root.GetProperty("eventId").GetGuid());
        Assert.True(root.TryGetProperty("occurredAt", out _));

        var payload = root.GetProperty("payload");
        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(ActingPm, payload.GetProperty("startedBy").GetGuid());
        Assert.Equal(1, payload.GetProperty("milestoneCount").GetInt32());
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
        ConsumerGroupId = $"construction-service-integration-{Guid.NewGuid():N}"
    };

    /// <summary>
    /// A project id inside this run's namespace, so the fixture's cleanup finds
    /// every row it leaves behind. Random rather than a fixed suffix, so no test
    /// here can collide with the hand-numbered ones elsewhere in the collection.
    /// </summary>
    private Guid NewProjectId() => _fixture.ProjectId(Guid.NewGuid().ToString("N")[..12]);

    private DesignEventsConsumer DesignConsumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IMilestoneSetupRepository>(_ => _fixture.Repository)
            .BuildServiceProvider();

        return new DesignEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(BrokerOptions()),
            NullLogger<DesignEventsConsumer>.Instance);
    }

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
    private OutboxDispatcher Dispatcher(KafkaConstructionEventPublisher publisher, Guid projectId)
    {
        var provider = new ServiceCollection()
            .AddScoped<IOutboxRepository>(_ => new SingleProjectOutbox(_fixture.OutboxRepository, projectId))
            .BuildServiceProvider();

        return new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions { PollIntervalSeconds = 1, BatchSize = 500 }),
            NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>
    /// A <c>DesignApproved</c> as the Design Service puts it on the wire: the agreed
    /// envelope around its full payload, not just the fields this service happens to
    /// read.
    /// </summary>
    private static string DesignApprovedEnvelope(
        Guid eventId,
        Guid projectId,
        Guid documentId,
        DateTimeOffset approvedAt) =>
        JsonSerializer.Serialize(new
        {
            eventType = "DesignApproved",
            eventId,
            occurredAt = approvedAt,
            payload = new
            {
                documentId,
                projectId,
                documentName = "Ground floor plan",
                versionId = Guid.NewGuid(),
                versionNumber = 2,
                approvedByUserId = Guid.NewGuid(),
                approvedAt
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
            GroupId = $"construction-service-integration-reader-{Guid.NewGuid():N}",
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
