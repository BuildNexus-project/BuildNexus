using System.Text.Json;
using BuildNexus.ConstructionService.Configuration;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// What <see cref="PaymentEventsConsumer.HandleAsync"/> asks of the settlement
/// repository for each message body, checked with a Moq mock (US-14, AC-4).
/// </summary>
/// <remarks>
/// The marker this consumer writes is the precondition for handover, so the
/// "must not write" cases matter most: only a <c>FinalPaymentSettled</c> may reach
/// the repository, and a message that is not one — or is broken — must leave it
/// untouched. A mock can prove that; a fake can only show the end state.
/// </remarks>
public class PaymentEventsConsumerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly DateTimeOffset SettledAt = new(2026, 9, 18, 14, 5, 0, TimeSpan.Zero);

    private readonly Mock<IPaymentSettlementRepository> _settlements = new();

    public static TheoryData<string> PayloadsMissingTheProject => new()
    {
        "{}",
        $"{{\"settledAt\":\"{SettledAt:O}\"}}",
        $"{{\"projectId\":\"{Guid.Empty}\",\"settledAt\":\"{SettledAt:O}\"}}"
    };

    [Fact]
    public async Task A_FinalPaymentSettled_records_the_settlement_once_with_the_ids_and_time_from_the_message()
    {
        StubRecord(recorded: true);

        await HandleAsync(Envelope("FinalPaymentSettled", SettledPayload()));

        _settlements.Verify(
            s => s.RecordSettlementIfAbsentAsync(
                ProjectId, EventId, SettledAt.UtcDateTime, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_FinalPaymentSettled_for_a_project_already_settled_is_absorbed_not_an_error()
    {
        // Duplicate event: the repository reports nothing new was written.
        StubRecord(recorded: false);

        var failure = await Record.ExceptionAsync(() => HandleAsync(Envelope("FinalPaymentSettled", SettledPayload())));

        Assert.Null(failure);
        _settlements.Verify(
            s => s.RecordSettlementIfAbsentAsync(
                ProjectId, EventId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_asks_the_repository_twice_with_the_same_event_id()
    {
        StubRecord(recorded: true);
        var message = Envelope("FinalPaymentSettled", SettledPayload());

        await HandleAsync(message);
        await HandleAsync(message);

        _settlements.Verify(
            s => s.RecordSettlementIfAbsentAsync(
                ProjectId, EventId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData("PaymentSettled")]
    [InlineData("PaymentReceived")]
    [InlineData("InvoiceGenerated")]
    [InlineData("finalpaymentsettled")]
    public async Task Anything_that_is_not_the_final_payment_is_ignored_and_never_marks_a_project_settled(
        string eventType)
    {
        // A part payment treated as final would hand a project over half paid.
        // The last one is the right name in the wrong case: the comparison is exact.
        await HandleAsync(Envelope(eventType, SettledPayload()));

        _settlements.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(PayloadsMissingTheProject))]
    public async Task A_FinalPaymentSettled_missing_its_project_is_a_json_failure_and_records_nothing(string payload)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(Envelope("FinalPaymentSettled", payload)));

        _settlements.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task A_body_that_will_never_parse_is_a_json_failure_and_records_nothing(string body)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(body));

        _settlements.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_settlement_write_surfaces_as_something_other_than_a_json_failure()
    {
        // So the caller leaves the offset uncommitted and the message is retried.
        _settlements
            .Setup(s => s.RecordSettlementIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("construction-db is unreachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandleAsync(Envelope("FinalPaymentSettled", SettledPayload())));
    }

    private void StubRecord(bool recorded) =>
        _settlements
            .Setup(s => s.RecordSettlementIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recorded);

    private static string SettledPayload() =>
        $"{{\"projectId\":\"{ProjectId}\",\"settledAt\":\"{SettledAt:O}\"}}";

    private static string Envelope(string eventType, string payloadJson) =>
        $"{{\"eventType\":\"{eventType}\",\"eventId\":\"{EventId}\","
        + $"\"occurredAt\":\"{SettledAt:O}\",\"payload\":{payloadJson}}}";

    private Task HandleAsync(string? body)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_settlements.Object);

        var consumer = new PaymentEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<PaymentEventsConsumer>.Instance);

        return consumer.HandleAsync(body, CancellationToken.None);
    }
}
