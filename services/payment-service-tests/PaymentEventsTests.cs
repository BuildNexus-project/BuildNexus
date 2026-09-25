using System.Text.Json;
using BuildNexus.PaymentService.Messaging;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The envelope and payload a recorded payment is announced with (US-16, AC-3).
/// </summary>
/// <remarks>
/// No database and no broker: what is under test is the JSON that will go on the
/// topic, which is the contract every consumer reads.
/// </remarks>
public class PaymentEventsTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid InvoiceId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid PayerId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly DateTime RecordedAt = new(2026, 9, 25, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_recorded_payment_is_announced_as_PaymentReceived()
    {
        var outboxEvent = PaymentEvents.Received(Payment(250m), ProjectId, InvoiceStatus.Pending);

        Assert.Equal(PaymentEventTypes.PaymentReceived, outboxEvent.EventType);
        Assert.Equal("PaymentReceived", Envelope(outboxEvent).GetProperty("eventType").GetString());
    }

    [Fact]
    public void The_event_is_keyed_by_project_and_traceable_to_its_invoice()
    {
        // The project is the Kafka message key, so every event about one project
        // lands on the same partition and arrives in order.
        var outboxEvent = PaymentEvents.Received(Payment(250m), ProjectId, InvoiceStatus.Pending);

        Assert.Equal(ProjectId, outboxEvent.ProjectId);
        Assert.Equal(InvoiceId, outboxEvent.InvoiceId);
    }

    [Fact]
    public void The_row_and_the_envelope_share_an_event_id()
    {
        // So a message on the topic can be traced back to the row that produced
        // it, and a redelivery is recognisable as the same event.
        var outboxEvent = PaymentEvents.Received(Payment(250m), ProjectId, InvoiceStatus.Pending);

        Assert.Equal(outboxEvent.Id, Envelope(outboxEvent).GetProperty("eventId").GetGuid());
        Assert.NotEqual(Guid.Empty, outboxEvent.Id);
    }

    [Fact]
    public void The_envelope_carries_the_agreed_four_top_level_fields()
    {
        // The shape every BuildNexus service publishes and consumes.
        var envelope = Envelope(PaymentEvents.Received(Payment(250m), ProjectId, InvoiceStatus.Pending));

        foreach (var field in (string[])["eventType", "eventId", "occurredAt", "payload"])
        {
            Assert.True(
                envelope.TryGetProperty(field, out _),
                $"The envelope is missing its '{field}' field.");
        }
    }

    [Fact]
    public void The_event_happened_when_the_payment_was_recorded_not_when_it_is_sent()
    {
        // occurredAt is taken off the payment, so it cannot drift from the row's
        // recorded_at however long the event waits on the outbox.
        var outboxEvent = PaymentEvents.Received(Payment(250m), ProjectId, InvoiceStatus.Pending);

        Assert.Equal(RecordedAt, outboxEvent.OccurredAt);
        Assert.Equal(
            RecordedAt,
            Envelope(outboxEvent).GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public void The_payload_carries_the_payment_its_invoice_its_project_and_its_payer()
    {
        var payload = Envelope(PaymentEvents.Received(Payment(250.75m), ProjectId, InvoiceStatus.Pending))
            .GetProperty("payload");

        Assert.Equal(InvoiceId, payload.GetProperty("invoiceId").GetGuid());
        Assert.Equal(ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(PayerId, payload.GetProperty("paidBy").GetGuid());
        Assert.Equal(250.75m, payload.GetProperty("amount").GetDecimal());
        Assert.NotEqual(Guid.Empty, payload.GetProperty("paymentId").GetGuid());
    }

    [Theory]
    [InlineData(InvoiceStatus.Pending, "Pending")]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    public void The_invoice_status_goes_on_the_wire_as_its_name_not_its_ordinal(
        InvoiceStatus status,
        string expected)
    {
        // This service is the first to put an enum on an event. The ordinal would
        // be a contract that silently changes meaning the moment a member is
        // inserted or reordered, in a consumer this service cannot see — and a
        // consumer reading `1` has no way to know it once meant Paid.
        var payload = Envelope(PaymentEvents.Received(Payment(250m), ProjectId, status))
            .GetProperty("payload");

        Assert.Equal(JsonValueKind.String, payload.GetProperty("invoiceStatus").ValueKind);
        Assert.Equal(expected, payload.GetProperty("invoiceStatus").GetString());
    }

    [Fact]
    public void The_payload_does_not_carry_the_outstanding_amount()
    {
        // Deliberately absent: it is this service's own derived figure, and a copy
        // on the topic goes stale the moment the next payment lands.
        var payload = Envelope(PaymentEvents.Received(Payment(250m), ProjectId, InvoiceStatus.Pending))
            .GetProperty("payload");

        Assert.False(payload.TryGetProperty("outstandingAmount", out _));
    }

    [Fact]
    public void Only_the_event_type_the_story_names_is_publishable()
    {
        // The database's own CHECK constraint is held to this same list, so an
        // invented event type fails at the write rather than on somebody's topic.
        Assert.Equal(["PaymentReceived"], PaymentEventTypes.All);
    }

    private static Payment Payment(decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = InvoiceId,
        Amount = amount,
        PaidBy = PayerId,
        RecordedAtUtc = RecordedAt
    };

    private static JsonElement Envelope(Models.OutboxEvent outboxEvent) =>
        JsonDocument.Parse(outboxEvent.Envelope).RootElement;
}
