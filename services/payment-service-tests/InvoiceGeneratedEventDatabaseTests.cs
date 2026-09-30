using System.Text.Json;
using BuildNexus.PaymentService.Messaging;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The <c>InvoiceGenerated</c> event US-24 adds, read back off <c>payment_outbox_events</c>
/// after a real invoice is raised — from both the manual path and the automatic one.
/// </summary>
/// <remarks>
/// Only the real engine can settle the case that matters most here: raising an invoice from a
/// redelivered <c>ConstructionStarted</c> must announce nothing the second time. That is a
/// claim about an <c>INSERT IGNORE</c> against a unique index and the transaction wrapped
/// around it, and a stub cannot refuse a duplicate.
/// <para>Needs <c>payment-db</c> running — see <see cref="PaymentDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PaymentDatabaseCollection.Name)]
public class InvoiceGeneratedEventDatabaseTests
{
    private static readonly Guid Staff = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private readonly PaymentDatabaseFixture _fixture;

    public InvoiceGeneratedEventDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Raising_an_invoice_by_hand_announces_it()
    {
        var projectId = _fixture.ProjectId("ea1");

        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 750_000m, Staff);

        var announced = Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoice.Id));

        Assert.Equal(PaymentEventTypes.InvoiceGenerated, announced.EventType);
        Assert.Equal(projectId, announced.ProjectId);
        Assert.False(announced.IsPublished);

        using var document = JsonDocument.Parse(announced.Envelope);
        var root = document.RootElement;

        Assert.Equal("InvoiceGenerated", root.GetProperty("eventType").GetString());
        Assert.Equal(announced.Id, root.GetProperty("eventId").GetGuid());

        var payload = root.GetProperty("payload");
        Assert.Equal(invoice.Id, payload.GetProperty("invoiceId").GetGuid());
        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(750_000m, payload.GetProperty("amount").GetDecimal());
        Assert.Equal(Staff, payload.GetProperty("createdBy").GetGuid());
        // Raised by a person, so there is no causing event — and the topic says so rather
        // than leaving a reader to infer it.
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("sourceEventId").ValueKind);
        Assert.Equal(TimeSpan.Zero, payload.GetProperty("generatedAt").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task An_invoice_raised_from_an_event_names_the_event_that_caused_it()
    {
        var projectId = _fixture.ProjectId("ea2");
        var sourceEventId = Guid.NewGuid();

        var invoice = await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 400_000m, Staff, sourceEventId);

        var announced = Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoice!.Id));

        using var document = JsonDocument.Parse(announced.Envelope);

        // The causation travels with the event, so the topic explains itself rather than
        // leaving a reader to correlate timestamps across two services.
        Assert.Equal(
            sourceEventId,
            document.RootElement.GetProperty("payload").GetProperty("sourceEventId").GetGuid());
    }

    [Fact]
    public async Task A_redelivered_ConstructionStarted_announces_nothing_a_second_time()
    {
        // The case this story turns on. Kafka delivers at least once, so the auto-invoicing
        // consumer will see the same ConstructionStarted twice — and the second pass must
        // neither raise a second invoice nor put a second InvoiceGenerated on the topic.
        var projectId = _fixture.ProjectId("ea3");
        var sourceEventId = Guid.NewGuid();

        var first = await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 400_000m, Staff, sourceEventId);
        var second = await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 400_000m, Staff, sourceEventId);

        Assert.NotNull(first);
        Assert.Null(second);

        // One invoice, one event. The rollback on the absorbed insert is what keeps the
        // second from reaching the outbox.
        Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(first.Id));
        Assert.Equal(1, await CountInvoiceGeneratedForProjectAsync(projectId));
    }

    [Fact]
    public async Task Both_invoice_paths_announce_the_same_event_type()
    {
        // A consumer reacting to billing has no interest in whether a person or the service
        // raised the invoice, so neither path may be the quiet one.
        var projectId = _fixture.ProjectId("ea4");

        await _fixture.InvoiceRepository.CreateAsync(projectId, 100_000m, Staff);
        await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 200_000m, Staff, Guid.NewGuid());

        Assert.Equal(2, await CountInvoiceGeneratedForProjectAsync(projectId));
    }

    [Fact]
    public async Task A_payment_still_announces_PaymentReceived_alongside_the_invoice_event()
    {
        // US-16's event is untouched by this story, and the two now sit on the same outbox for
        // one invoice — in the order they happened, which is what a consumer reads.
        var projectId = _fixture.ProjectId("ea5");
        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 500_000m, Staff);

        await _fixture.PaymentRepository.RecordPaymentAsync(invoice.Id, 500_000m, Guid.NewGuid());

        var events = await _fixture.OutboxRepository.ListForInvoiceAsync(invoice.Id);

        Assert.Collection(
            events,
            first => Assert.Equal(PaymentEventTypes.InvoiceGenerated, first.EventType),
            second => Assert.Equal(PaymentEventTypes.PaymentReceived, second.EventType));
    }

    /// <summary>How many <c>InvoiceGenerated</c> events this run's project has on the outbox.</summary>
    private async Task<int> CountInvoiceGeneratedForProjectAsync(Guid projectId)
    {
        var pending = await _fixture.OutboxRepository.ListPendingAsync(batchSize: 1000);

        return pending.Count(e =>
            e.ProjectId == projectId && e.EventType == PaymentEventTypes.InvoiceGenerated);
    }
}
