using System.Text.Json;
using BuildNexus.DesignService.Configuration;
using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Messaging;
using BuildNexus.DesignService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Sdk;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The Design Service's publish path against a real broker and a real MySQL, with
/// nothing stubbed between them (US-30).
/// </summary>
/// <remarks>
/// The Design Service only publishes — nothing in it reads a topic — so this is the
/// one Kafka path it has. Every other class in this project that touches events does
/// so over a fake, which is right for the decisions they cover, but a fake cannot show
/// that a message actually crosses the wire or that the envelope survives a MySQL
/// column and a broker unchanged. This test exists for exactly that: a design version
/// and its <c>DesignSubmitted</c> are written to MySQL in one transaction, the real
/// <see cref="OutboxDispatcher"/> and <see cref="KafkaDesignEventPublisher"/> send it,
/// and an independent consumer reads it back off <c>design-events</c> — while the
/// outbox row is marked published in the same database.
/// <para>
/// Needs <c>design-db</c> and <c>kafka</c> running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait design-db kafka</c>. The broker
/// address comes from <c>Kafka__BootstrapServers</c>, the same name the service itself
/// reads, and falls back to the compose broker's host listener. CI starts both as its
/// own step.
/// </para>
/// <para>
/// The topic name is fixed by the service, so on a machine whose full stack is running
/// this test publishes onto the topic that stack's own consumers are reading — the
/// Construction Service reads <c>design-events</c>. Point <c>Kafka__BootstrapServers</c>
/// at a disposable broker to avoid that; on CI the broker is new for the run.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(DesignDatabaseCollection.Name)]
public class DesignKafkaIntegrationTests
{
    /// <summary>
    /// How long a step may take to show up before the test gives up. Generous on
    /// purpose: a fresh broker creates a topic and elects a group coordinator on
    /// first use, and a test that fails on a slow runner proves nothing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Whole seconds, because that is all a MySQL <c>DATETIME</c> keeps.</summary>
    private static readonly DateTime UploadedAt = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly DesignDatabaseFixture _fixture;

    public DesignKafkaIntegrationTests(DesignDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------ publish: MySQL -> Kafka ----

    [Fact]
    public async Task An_event_enqueued_with_a_design_version_is_dispatched_to_design_events_and_marked_published()
    {
        OutboxEvent? raised = null;

        // The real write: document, version and the DesignSubmitted event in one
        // transaction, exactly as the upload endpoint does it. The callback keeps a
        // reference to the event so the broker's copy can be compared with it.
        var stored = await _fixture.Repository.AddVersionAsync(
            NewUpload(),
            (document, version) =>
            {
                raised = DesignEvents.Submitted(document, version);
                return [raised];
            });

        Assert.NotNull(raised);
        var documentId = stored.Document.Id;

        using (var publisher = new KafkaDesignEventPublisher(
            Options.Create(BrokerOptions()),
            NullLogger<KafkaDesignEventPublisher>.Instance))
        {
            var dispatcher = Dispatcher(publisher, documentId);
            await dispatcher.StartAsync(CancellationToken.None);

            try
            {
                await WaitUntilAsync(
                    async () => (await _fixture.Outbox.ListForDocumentAsync(documentId)).All(e => e.IsPublished),
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
        var row = Assert.Single(await _fixture.Outbox.ListForDocumentAsync(documentId));
        Assert.True(row.IsPublished);
        Assert.True(row.AttemptCount >= 1);
        Assert.Null(row.LastError);

        // The broker side, read by a consumer that shares nothing with the service:
        // a fresh group, from the start of the topic, matching on the message key —
        // which for this service is the document id, not the project id.
        var message = ReadFromStart(
            KafkaDesignEventPublisher.Topic,
            m => m.Message.Key == documentId.ToString());

        // Byte for byte what was written to MySQL — through the column, the
        // dispatcher and the broker — so a retry can never mint a new eventId.
        Assert.Equal(raised!.Envelope, message.Message.Value);

        // And it is the agreed envelope, not merely equal to whatever was stored.
        using var envelope = JsonDocument.Parse(message.Message.Value);
        var root = envelope.RootElement;
        Assert.Equal(DesignEventTypes.DesignSubmitted, root.GetProperty("eventType").GetString());
        Assert.Equal(raised.Id, root.GetProperty("eventId").GetGuid());
        Assert.True(root.TryGetProperty("occurredAt", out _));

        var payload = root.GetProperty("payload");
        Assert.Equal(documentId, payload.GetProperty("documentId").GetGuid());
        Assert.Equal(stored.Document.ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(stored.Version.Id, payload.GetProperty("versionId").GetGuid());
        Assert.Equal(1, payload.GetProperty("versionNumber").GetInt32());
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
        MessageTimeoutMs = 10_000
    };

    private static DesignUpload NewUpload() => new()
    {
        // Unique per test, so the document is new rather than a version of one an
        // earlier test left behind in a database every test in the collection shares.
        ProjectId = Guid.NewGuid(),
        // Named so the fixture's cleanup finds it when the collection is done.
        DocumentName = $"{DesignDatabaseFixture.TestDocumentPrefix}{Guid.NewGuid():N}-plan",
        UploadedBy = Guid.NewGuid(),
        FileName = "plan.pdf",
        ContentType = "application/pdf",
        Content = "%PDF-"u8.ToArray(),
        UploadedAtUtc = UploadedAt
    };

    /// <summary>
    /// The real dispatcher, over the real outbox — narrowed to one document.
    /// </summary>
    /// <remarks>
    /// Left alone it would drain every pending row in the table, and the table is
    /// shared: other tests leave pending events behind until the collection ends,
    /// and a developer's own database may hold real ones. Sending those to whatever
    /// broker this run points at, and marking them delivered, would be a side effect
    /// no test should have.
    /// </remarks>
    private OutboxDispatcher Dispatcher(KafkaDesignEventPublisher publisher, Guid documentId)
    {
        var provider = new ServiceCollection()
            .AddScoped<IOutboxRepository>(_ => new SingleDocumentOutbox(_fixture.Outbox, documentId))
            .BuildServiceProvider();

        return new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions { PollIntervalSeconds = 1, BatchSize = 500 }),
            NullLogger<OutboxDispatcher>.Instance);
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
            // A group of its own per read, so a second run never inherits the offset
            // a first one committed and skips the message it is here to see.
            GroupId = $"design-service-integration-reader-{Guid.NewGuid():N}",
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
    /// Polls until <paramref name="condition"/> holds. The dispatcher works on its own
    /// thread, so the only honest way to wait for it is to look for what it leaves in
    /// the database.
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
    /// The real outbox with its pending list cut down to one document's events.
    /// Everything else — marking published, marking failed — goes straight through
    /// to MySQL.
    /// </summary>
    private sealed class SingleDocumentOutbox : IOutboxRepository
    {
        private readonly IOutboxRepository _inner;
        private readonly Guid _documentId;

        public SingleDocumentOutbox(IOutboxRepository inner, Guid documentId)
        {
            _inner = inner;
            _documentId = documentId;
        }

        public async Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            (await _inner.ListPendingAsync(batchSize, cancellationToken))
                .Where(e => e.DocumentId == _documentId)
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

        public Task<IReadOnlyList<OutboxEvent>> ListForDocumentAsync(
            Guid documentId,
            CancellationToken cancellationToken = default) =>
            _inner.ListForDocumentAsync(documentId, cancellationToken);
    }
}
