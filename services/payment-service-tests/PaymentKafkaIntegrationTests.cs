using System.Text.Json;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using BuildNexus.PaymentService.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Sdk;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The Payment Service's two Kafka paths against a real broker and a real MySQL, with
/// nothing stubbed between them (US-30).
/// </summary>
/// <remarks>
/// Every other class in this project that touches Kafka does so over a fake, which is
/// right for the decisions they cover — but a fake cannot show that a message actually
/// crosses the wire, that the envelope survives a MySQL column and a broker unchanged,
/// or that a consumer's commit and rebalance behave. These two tests exist for exactly
/// that:
/// <list type="bullet">
/// <item><b>Consume:</b> a <c>ConstructionStarted</c> is produced onto
/// <c>construction-events</c> in the Construction Service's wire format, the real
/// <see cref="ConstructionEventsConsumer"/> reads it, and the invoice it raises against
/// the project's quotation appears in MySQL. This is the cross-service link: what the
/// Construction Service publishes is what starts billing here.</item>
/// <item><b>Publish:</b> a payment is recorded through the real
/// <see cref="PaymentRepository"/>, which enqueues <c>PaymentReceived</c> in the same
/// transaction as the money; the real <see cref="OutboxDispatcher"/> and
/// <see cref="KafkaPaymentEventPublisher"/> send it, and an independent consumer reads it
/// back off <c>payment-events</c> — while the outbox row is marked published in the same
/// database.</item>
/// </list>
/// <para>
/// The service's other consumer, <c>project-events</c>, shares the consume loop the first
/// test drives and is not repeated here.
/// </para>
/// <para>
/// Needs <c>payment-db</c> and <c>kafka</c> running:
/// <c>cd infra &amp;&amp; docker compose up -d --wait payment-db kafka</c>. The broker
/// address comes from <c>Kafka__BootstrapServers</c>, the same name the service itself
/// reads, and falls back to the compose broker's host listener. CI starts both as its own
/// step.
/// </para>
/// <para>
/// The topic names are fixed by the service, so on a machine whose full stack is running
/// these tests publish onto the topics that stack's own consumers are reading. Point
/// <c>Kafka__BootstrapServers</c> at a disposable broker to avoid that; on CI the broker is
/// new for the run.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PaymentDatabaseCollection.Name)]
public class PaymentKafkaIntegrationTests
{
    /// <summary>
    /// How long a step may take to show up before the test gives up. Generous on
    /// purpose: a fresh broker creates a topic and elects a group coordinator on
    /// first use, and a test that fails on a slow runner proves nothing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private const string ConstructionEventsTopic = "construction-events";

    private readonly PaymentDatabaseFixture _fixture;

    public PaymentKafkaIntegrationTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------------ consume: Kafka -> MySQL ----

    [Fact]
    public async Task A_ConstructionStarted_event_on_construction_events_raises_an_invoice_for_the_quoted_total_in_MySQL()
    {
        var projectId = NewProjectId();
        var eventId = Guid.NewGuid();
        var startedBy = Guid.NewGuid();

        // The consumer raises an invoice for the project's current quotation, so one
        // has to exist. A figure with pence, so the DECIMAL(15,2) round trip through
        // the consumer, the invoice INSERT and the read back is an exact one.
        var quotation = await _fixture.QuotationRepository.CreateAsync(projectId, 1_234_567.89m, Guid.NewGuid());

        // Produced first, so the topic exists by the time the consumer subscribes on a
        // broker that has never seen it. The Construction Service is not referenced
        // from here — this is its wire format, which is the contract this service
        // actually depends on.
        await ProduceAsync(
            ConstructionEventsTopic,
            projectId.ToString(),
            ConstructionStartedEnvelope(eventId, projectId, startedBy));

        var consumer = ConstructionConsumer();
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                async () => (await _fixture.InvoiceRepository.ListForProjectAsync(projectId)).Count > 0,
                "the consumer to raise the invoice");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        // Read back through the real repository, so this is what MySQL holds and not
        // what the consumer believes it wrote.
        var invoice = Assert.Single(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));
        Assert.Equal(quotation.EstimatedTotal, invoice.Amount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        // Billed to the Project Manager the event names, never a placeholder.
        Assert.Equal(startedBy, invoice.CreatedBy);
        // The event's own id is what lets a redelivery be recognised and absorbed.
        Assert.Equal(eventId, invoice.SourceEventId);
    }

    // ------------------------------------------ publish: MySQL -> Kafka ----

    [Fact]
    public async Task Recording_a_payment_enqueues_PaymentReceived_which_is_dispatched_to_payment_events_and_marked_published()
    {
        var projectId = NewProjectId();
        var paidBy = Guid.NewGuid();

        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 1_000m, Guid.NewGuid());

        // The real transaction: the payment, the invoice's move to Paid and the event
        // all commit together. Its event is enqueued inside that transaction — there
        // is no callback or argument to hand one in — so the stored row is the only
        // place to read what was raised.
        var recorded = await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 1_000m, paidBy);
        Assert.Equal(PaymentRecordingOutcome.Recorded, recorded.Outcome);

        var stored = Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoice.Id));
        Assert.Equal(PaymentEventTypes.PaymentReceived, stored.EventType);
        Assert.False(stored.IsPublished);

        using (var publisher = new KafkaPaymentEventPublisher(
            Options.Create(BrokerOptions()),
            NullLogger<KafkaPaymentEventPublisher>.Instance))
        {
            var dispatcher = Dispatcher(publisher, invoice.Id);
            await dispatcher.StartAsync(CancellationToken.None);

            try
            {
                await WaitUntilAsync(
                    async () => (await _fixture.OutboxRepository.ListForInvoiceAsync(invoice.Id)).All(e => e.IsPublished),
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
        var row = Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoice.Id));
        Assert.True(row.IsPublished);
        Assert.True(row.AttemptCount >= 1);
        Assert.Null(row.LastError);

        // The broker side, read by a consumer that shares nothing with the service:
        // a fresh group, from the start of the topic, matching on the message key —
        // the project id, so a project's payments stay in order on one partition.
        var message = ReadFromStart(
            KafkaPaymentEventPublisher.Topic,
            m => m.Message.Key == projectId.ToString());

        // Byte for byte what was stored in MySQL before dispatch — through the
        // column, the dispatcher and the broker — so a retry can never mint a new
        // eventId.
        Assert.Equal(stored.Envelope, message.Message.Value);

        // And it is the agreed envelope, with the fields other services read, not
        // merely equal to whatever was stored.
        using var envelope = JsonDocument.Parse(message.Message.Value);
        var root = envelope.RootElement;
        Assert.Equal(PaymentEventTypes.PaymentReceived, root.GetProperty("eventType").GetString());
        Assert.Equal(stored.Id, root.GetProperty("eventId").GetGuid());
        Assert.True(root.TryGetProperty("occurredAt", out _));

        var payload = root.GetProperty("payload");
        Assert.Equal(recorded.Payment!.Id, payload.GetProperty("paymentId").GetGuid());
        Assert.Equal(invoice.Id, payload.GetProperty("invoiceId").GetGuid());
        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(1_000m, payload.GetProperty("amount").GetDecimal());
        Assert.Equal(paidBy, payload.GetProperty("paidBy").GetGuid());
        // A full payment, so the event says the invoice is now settled — the fact a
        // consumer cannot derive from the payment alone.
        Assert.Equal("Paid", payload.GetProperty("invoiceStatus").GetString());
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
        ConsumerGroupId = $"payment-service-integration-{Guid.NewGuid():N}"
    };

    /// <summary>
    /// A project id inside this run's namespace, so the fixture's cleanup finds
    /// every row it leaves behind. Random rather than a fixed suffix, so no test
    /// here can collide with the hand-numbered ones elsewhere in the collection.
    /// </summary>
    private Guid NewProjectId() => _fixture.ProjectId(Guid.NewGuid().ToString("N")[..12]);

    private ConstructionEventsConsumer ConstructionConsumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IQuotationRepository>(_ => _fixture.QuotationRepository)
            .AddScoped<IInvoiceRepository>(_ => _fixture.InvoiceRepository)
            .BuildServiceProvider();

        return new ConstructionEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(BrokerOptions()),
            NullLogger<ConstructionEventsConsumer>.Instance);
    }

    /// <summary>
    /// The real dispatcher, over the real outbox — narrowed to one invoice.
    /// </summary>
    /// <remarks>
    /// Left alone it would drain every pending row in the table, and the table is
    /// shared: other tests leave pending events behind until the collection ends,
    /// and a developer's own database may hold real ones. Sending those to whatever
    /// broker this run points at, and marking them delivered, would be a side effect
    /// no test should have.
    /// </remarks>
    private OutboxDispatcher Dispatcher(KafkaPaymentEventPublisher publisher, Guid invoiceId)
    {
        var provider = new ServiceCollection()
            .AddScoped<IOutboxRepository>(_ => new SingleInvoiceOutbox(_fixture.OutboxRepository, invoiceId))
            .BuildServiceProvider();

        return new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions { PollIntervalSeconds = 1, BatchSize = 500 }),
            NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>
    /// A <c>ConstructionStarted</c> as the Construction Service puts it on the wire:
    /// the agreed envelope around its full payload, not just the fields this service
    /// happens to read.
    /// </summary>
    private static string ConstructionStartedEnvelope(Guid eventId, Guid projectId, Guid startedBy)
    {
        var occurredAt = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);

        return JsonSerializer.Serialize(new
        {
            eventType = "ConstructionStarted",
            eventId,
            occurredAt,
            payload = new
            {
                projectId,
                startedAt = occurredAt,
                milestoneCount = 7,
                startedBy
            }
        });
    }

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
            GroupId = $"payment-service-integration-reader-{Guid.NewGuid():N}",
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
    /// The real outbox with its pending list cut down to one invoice's events.
    /// Everything else — marking published, marking failed — goes straight through
    /// to MySQL.
    /// </summary>
    private sealed class SingleInvoiceOutbox : IOutboxRepository
    {
        private readonly IOutboxRepository _inner;
        private readonly Guid _invoiceId;

        public SingleInvoiceOutbox(IOutboxRepository inner, Guid invoiceId)
        {
            _inner = inner;
            _invoiceId = invoiceId;
        }

        public async Task<IReadOnlyList<OutboxEvent>> ListPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            (await _inner.ListPendingAsync(batchSize, cancellationToken))
                .Where(e => e.InvoiceId == _invoiceId)
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

        public Task<IReadOnlyList<OutboxEvent>> ListForInvoiceAsync(
            Guid invoiceId,
            CancellationToken cancellationToken = default) =>
            _inner.ListForInvoiceAsync(invoiceId, cancellationToken);
    }
}
