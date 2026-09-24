using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="PaymentEventsConsumer.HandleAsync"/> — what one message off
/// <c>payment-events</c> does. The marker it writes is AC-4's handover precondition,
/// so the cases that matter most are the ones where it must <em>not</em> write:
/// anything that is not a final payment.
/// </summary>
/// <remarks>
/// The Payment Service is unbuilt, so these tests are written against US-14's
/// proposed <c>FinalPaymentSettled</c> contract. They are what will catch the
/// difference if that service ends up publishing something else.
/// </remarks>
public class PaymentEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly DateTimeOffset SettledAt = new(2026, 9, 18, 14, 5, 0, TimeSpan.Zero);

    private readonly FakePaymentSettlementRepository _repository = new();

    [Fact]
    public async Task FinalPaymentSettled_records_the_marker_from_the_envelope_and_payload()
    {
        await Consumer().HandleAsync(
            Envelope("FinalPaymentSettled", new { projectId = ProjectId, settledAt = SettledAt }),
            CancellationToken.None);

        var call = Assert.Single(_repository.Calls);
        Assert.Equal(ProjectId, call.ProjectId);
        // The event id is kept so a marker can be traced back to the message.
        Assert.Equal(EventId, call.SourceEventId);
        // When the payment settled, not when this service read the message.
        Assert.Equal(SettledAt.UtcDateTime, call.SettledAtUtc);
    }

    [Fact]
    public async Task A_project_whose_settlement_is_already_recorded_is_not_an_error()
    {
        // Kafka delivers at least once. A redelivery must be absorbed rather than
        // wedge the partition.
        _repository.RecordResult = false;

        await Consumer().HandleAsync(
            Envelope("FinalPaymentSettled", new { projectId = ProjectId, settledAt = SettledAt }),
            CancellationToken.None);

        Assert.Single(_repository.Calls);
    }

    [Fact]
    public async Task A_plain_PaymentSettled_is_not_treated_as_the_final_payment()
    {
        // The case worth being strict about: AC-4 gates handover on the *final*
        // payment. An instalment event with a similar name must not open that gate, or
        // a project could be handed over part paid — and nothing downstream could
        // detect that it had been.
        await Consumer().HandleAsync(
            Envelope("PaymentSettled", new { projectId = ProjectId, settledAt = SettledAt }),
            CancellationToken.None);

        Assert.Empty(_repository.Calls);
    }

    [Theory]
    [InlineData("InvoiceRaised")]
    [InlineData("PaymentReceived")]
    [InlineData("PaymentRefunded")]
    [InlineData("SomethingElseEntirely")]
    public async Task An_event_that_is_not_a_final_settlement_is_left_alone(string eventType)
    {
        await Consumer().HandleAsync(
            Envelope(eventType, new { projectId = ProjectId, settledAt = SettledAt }),
            CancellationToken.None);

        Assert.Empty(_repository.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json at all {")]
    [InlineData("{ \"eventType\": \"FinalPaymentSettled\" ")]
    public async Task A_message_that_cannot_be_parsed_throws_JsonException(string body)
    {
        // ProcessAsync maps a JsonException to "commit past it": a malformed message
        // will not parse on a retry, and holding the partition on it would stop
        // everything behind it.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(body, CancellationToken.None));
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_FinalPaymentSettled_with_no_payload_object_throws_JsonException()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            eventType = "FinalPaymentSettled",
            eventId = EventId,
            occurredAt = SettledAt,
            payload = (object?)null
        });

        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(envelope, CancellationToken.None));
        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_FinalPaymentSettled_missing_its_project_id_throws_JsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("FinalPaymentSettled", new { settledAt = SettledAt }),
            CancellationToken.None));

        Assert.Empty(_repository.Calls);
    }

    [Fact]
    public async Task A_repository_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        // ProcessAsync catches this, logs it, and does NOT commit — the message is
        // re-read and retried on the next poll. Missing a settlement would leave a
        // finished project permanently unable to be handed over, so this is the one
        // failure worth retrying hardest.
        _repository.RecordThrows = new InvalidOperationException("construction-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            Envelope("FinalPaymentSettled", new { projectId = ProjectId, settledAt = SettledAt }),
            CancellationToken.None));
    }

    private static string Envelope(string eventType, object payload) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId = EventId,
            occurredAt = SettledAt,
            payload
        });

    private PaymentEventsConsumer Consumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IPaymentSettlementRepository>(_ => _repository)
            .BuildServiceProvider();

        return new PaymentEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:29092",
                ConsumerGroupId = "construction-service-tests"
            }),
            NullLogger<PaymentEventsConsumer>.Instance);
    }
}
