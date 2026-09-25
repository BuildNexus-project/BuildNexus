using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using BuildNexus.PaymentService.Models;
using MySqlConnector;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// That a recorded payment is announced, and a refused one is not (US-16, AC-3).
/// </summary>
/// <remarks>
/// The outbox row is written inside the payment's own transaction, so reading it
/// back afterwards is the only honest way to show the two commit together — a
/// stub could show the call was made, not that it committed with the money.
/// </remarks>
[Collection(PaymentDatabaseCollection.Name)]
public class PaymentOutboxDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public PaymentOutboxDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Recording_a_payment_enqueues_a_PaymentReceived()
    {
        // AC-3, the whole bullet.
        var (invoiceId, _) = await InvoiceFor("e01", 1_000m);

        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 400m, Guid.NewGuid());

        var announced = Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId));
        Assert.Equal(PaymentEventTypes.PaymentReceived, announced.EventType);
        Assert.False(announced.IsPublished);
        Assert.Equal(0, announced.AttemptCount);
    }

    [Fact]
    public async Task The_event_is_keyed_by_the_invoices_project()
    {
        // Read off the invoice inside the same transaction, so the key cannot
        // disagree with the row it came from.
        var (invoiceId, projectId) = await InvoiceFor("e02", 500m);

        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 500m, Guid.NewGuid());

        var announced = Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId));
        Assert.Equal(projectId, announced.ProjectId);
    }

    [Fact]
    public async Task Each_partial_payment_is_announced_in_the_order_it_was_made()
    {
        // A consumer that saw the settling payment before the partial one that
        // preceded it would have to reason about a balance that went up.
        var (invoiceId, _) = await InvoiceFor("e03", 1_000m);

        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 400m, Guid.NewGuid());
        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 600m, Guid.NewGuid());

        var announced = await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId);

        Assert.Equal(2, announced.Count);
        Assert.True(announced[0].SequenceNumber < announced[1].SequenceNumber);
        Assert.Equal(400m, AmountOf(announced[0]));
        Assert.Equal(600m, AmountOf(announced[1]));
    }

    [Fact]
    public async Task The_settling_payment_announces_the_invoice_as_Paid()
    {
        // The one fact about the invoice a consumer cannot derive from the
        // payment alone.
        var (invoiceId, _) = await InvoiceFor("e04", 1_000m);

        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 400m, Guid.NewGuid());
        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 600m, Guid.NewGuid());

        var announced = await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId);

        Assert.Contains("\"invoiceStatus\":\"Pending\"", announced[0].Envelope, StringComparison.Ordinal);
        Assert.Contains("\"invoiceStatus\":\"Paid\"", announced[1].Envelope, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(1_000.01)]
    public async Task A_payment_refused_by_the_cap_announces_nothing(decimal amount)
    {
        // The other half of "commit together or not at all": no money moved, so
        // no consumer should ever hear that any did.
        var (invoiceId, _) = await InvoiceFor("e05", 1_000m);

        var result = await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, amount, Guid.NewGuid());

        Assert.Equal(PaymentRecordingOutcome.ExceedsOutstanding, result.Outcome);
        Assert.Empty(await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId));
    }

    [Fact]
    public async Task A_payment_against_a_settled_invoice_announces_nothing()
    {
        var (invoiceId, _) = await InvoiceFor("e06", 300m);
        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 300m, Guid.NewGuid());

        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 50m, Guid.NewGuid());

        // Exactly one event: the payment that actually happened.
        Assert.Single(await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId));
    }

    [Fact]
    public async Task A_pending_event_is_waiting_for_the_dispatcher()
    {
        // The dispatcher's own query. Nothing sends yet — that is the next
        // commit — so a freshly recorded payment must show up here.
        var (invoiceId, _) = await InvoiceFor("e07", 100m);

        await _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, 100m, Guid.NewGuid());

        var pending = await _fixture.OutboxRepository.ListPendingAsync(batchSize: 100);

        Assert.Contains(pending, outboxEvent => outboxEvent.InvoiceId == invoiceId);
    }

    [Fact]
    public async Task The_database_refuses_an_event_type_the_story_does_not_name()
    {
        // ck_payment_outbox_events_type is the database half of PaymentEventTypes.
        // Asserted by going around the repository, since the repository cannot
        // produce another type — which is the point: the constraint is what stops
        // anything else that reaches this table, so an invented event fails at the
        // write rather than on somebody else's topic.
        var (invoiceId, projectId) = await InvoiceFor("e08", 100m);

        await using var connection = new MySqlConnection(PaymentDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO payment_outbox_events
                (id, project_id, invoice_id, event_type, envelope, occurred_at)
            VALUES
                (@id, @projectId, @invoiceId, 'FinalPaymentSettled', '{}', UTC_TIMESTAMP(6));";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());
        command.Parameters.AddWithValue("@projectId", projectId);
        command.Parameters.AddWithValue("@invoiceId", invoiceId);

        await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Empty(await _fixture.OutboxRepository.ListForInvoiceAsync(invoiceId));
    }

    /// <summary>An invoice of <paramref name="amount"/>, with the project it belongs to.</summary>
    private async Task<(Guid InvoiceId, Guid ProjectId)> InvoiceFor(string suffix, decimal amount)
    {
        var projectId = _fixture.ProjectId(suffix);
        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, amount, Guid.NewGuid());

        return (invoice.Id, projectId);
    }

    private static decimal AmountOf(OutboxEvent outboxEvent) =>
        System.Text.Json.JsonDocument.Parse(outboxEvent.Envelope)
            .RootElement.GetProperty("payload").GetProperty("amount").GetDecimal();
}
