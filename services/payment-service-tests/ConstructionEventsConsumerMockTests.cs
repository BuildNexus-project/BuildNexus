using System.Text.Json;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using BuildNexus.PaymentService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="ConstructionEventsConsumer.HandleAsync"/> asks of its three
/// repositories for each message body, checked with Moq mocks (AC-2).
/// </summary>
/// <remarks>
/// The older <see cref="ConstructionEventsConsumerTests"/> check the result in
/// fakes. These check the interactions: a good message makes exactly the one call
/// it should, with the ids from the message, and every message that must not bill
/// anyone (wrong type, bad payload, no quotation) leaves the repositories
/// untouched. The "never touched" checks are what a fake cannot give.
/// </remarks>
public class ConstructionEventsConsumerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StartedBy = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset CompletedAt = new(2026, 8, 31, 14, 5, 0, TimeSpan.Zero);

    private readonly Mock<IQuotationRepository> _quotations = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IConstructionCompletionRepository> _completions = new();

    public static TheoryData<decimal> QuotationTotals => new()
    {
        0.01m,
        1m,
        500_000m,
        1_100_000.50m,
        9_999_999_999_999.99m
    };

    public static TheoryData<string> StartedPayloadsMissingAnId => new()
    {
        "{}",
        $"{{\"projectId\":\"{ProjectId}\"}}",
        $"{{\"startedBy\":\"{StartedBy}\"}}",
        $"{{\"projectId\":\"{Guid.Empty}\",\"startedBy\":\"{StartedBy}\"}}",
        $"{{\"projectId\":\"{ProjectId}\",\"startedBy\":\"{Guid.Empty}\"}}"
    };

    // ------------------------------------------------ ConstructionStarted ----

    [Fact]
    public async Task A_ConstructionStarted_bills_the_current_quotation_once_with_the_causing_event_id()
    {
        StubQuotation(1_100_000m);
        StubInvoiceRaised();

        await HandleAsync(ConstructionStarted());

        _invoices.Verify(
            i => i.CreateFromEventIfAbsentAsync(
                ProjectId, 1_100_000m, StartedBy, EventId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(QuotationTotals))]
    public async Task The_invoice_amount_is_always_the_quotation_total(decimal total)
    {
        StubQuotation(total);
        StubInvoiceRaised();

        await HandleAsync(ConstructionStarted());

        _invoices.Verify(
            i => i.CreateFromEventIfAbsentAsync(
                ProjectId, total, StartedBy, EventId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_project_with_no_quotation_is_skipped_without_failing_and_nothing_is_billed()
    {
        _quotations
            .Setup(q => q.GetCurrentForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Quotation?)null);

        var failure = await Record.ExceptionAsync(() => HandleAsync(ConstructionStarted()));

        Assert.Null(failure);
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_ConstructionStarted_the_repository_has_already_billed_is_absorbed_not_an_error()
    {
        // Duplicate event: the repository answers "no new invoice" (null).
        StubQuotation(750_000m);
        _invoices
            .Setup(i => i.CreateFromEventIfAbsentAsync(
                ProjectId, 750_000m, StartedBy, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);

        var failure = await Record.ExceptionAsync(() => HandleAsync(ConstructionStarted()));

        Assert.Null(failure);
        _invoices.Verify(
            i => i.CreateFromEventIfAbsentAsync(
                ProjectId, 750_000m, StartedBy, EventId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_reaches_the_repository_twice_with_the_same_event_id()
    {
        // The consumer does not decide what is a duplicate — the unique index
        // behind the repository does. What the consumer owes it is the same
        // eventId every time, so the index can recognise the repeat.
        StubQuotation(750_000m);
        StubInvoiceRaised();
        var message = ConstructionStarted();

        await HandleAsync(message);
        await HandleAsync(message);

        _invoices.Verify(
            i => i.CreateFromEventIfAbsentAsync(
                ProjectId, 750_000m, StartedBy, EventId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [MemberData(nameof(StartedPayloadsMissingAnId))]
    public async Task A_ConstructionStarted_missing_or_zeroing_an_id_is_a_json_failure_and_touches_nothing(
        string payload)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(Envelope("ConstructionStarted", EventId, payload)));

        _quotations.VerifyNoOtherCalls();
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_envelope_with_no_event_id_is_refused_before_any_repository_is_asked()
    {
        // Without the eventId a retry could bill the project again.
        var message = Envelope("ConstructionStarted", Guid.Empty, StartedPayload());

        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(message));

        _quotations.VerifyNoOtherCalls();
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_invoice_write_surfaces_as_something_other_than_a_json_failure()
    {
        // So the caller leaves the offset uncommitted and the message is retried.
        StubQuotation(500_000m);
        _invoices
            .Setup(i => i.CreateFromEventIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("payment-db is unreachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(ConstructionStarted()));
    }

    // ------------------------------------------------ other event types ----

    [Theory]
    [InlineData("ProjectCreated")]
    [InlineData("ConstructionUpdated")]
    [InlineData("MilestoneCompleted")]
    [InlineData("constructionstarted")]
    public async Task Any_other_event_type_is_ignored_and_touches_nothing(string eventType)
    {
        // The last one is the right name in the wrong case: the comparison is
        // exact, so it is ignored rather than billed.
        await HandleAsync(Envelope(eventType, EventId, StartedPayload()));

        _quotations.VerifyNoOtherCalls();
        _invoices.VerifyNoOtherCalls();
        _completions.VerifyNoOtherCalls();
    }

    // ---------------------------------------------- ConstructionCompleted ----

    [Fact]
    public async Task A_ConstructionCompleted_records_the_completion_once_and_never_raises_an_invoice()
    {
        StubCompletion(announced: false);

        await HandleAsync(Envelope("ConstructionCompleted", EventId, CompletedPayload()));

        _completions.Verify(
            c => c.RecordCompletionAndMaybeAnnounceAsync(
                ProjectId,
                new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc),
                EventId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _invoices.VerifyNoOtherCalls();
        _quotations.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_ConstructionCompleted_is_handled_the_same_whether_or_not_it_announced_the_settlement(
        bool announced)
    {
        // Announced: the project was already paid up. Not announced: it still
        // owes, or this is a redelivery. Neither is an error.
        StubCompletion(announced);

        var failure = await Record.ExceptionAsync(
            () => HandleAsync(Envelope("ConstructionCompleted", EventId, CompletedPayload())));

        Assert.Null(failure);
        _completions.Verify(
            c => c.RecordCompletionAndMaybeAnnounceAsync(
                ProjectId, It.IsAny<DateTime>(), EventId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_ConstructionCompleted_with_a_zero_project_id_is_a_json_failure_and_records_nothing()
    {
        var payload = $"{{\"projectId\":\"{Guid.Empty}\",\"completedAt\":\"{CompletedAt:O}\"}}";

        await Assert.ThrowsAsync<JsonException>(
            () => HandleAsync(Envelope("ConstructionCompleted", EventId, payload)));

        _completions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_completion_write_surfaces_as_something_other_than_a_json_failure()
    {
        _completions
            .Setup(c => c.RecordCompletionAndMaybeAnnounceAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("payment-db is unreachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandleAsync(Envelope("ConstructionCompleted", EventId, CompletedPayload())));
    }

    // -------------------------------------------------------------- helpers ----

    private void StubQuotation(decimal total) =>
        _quotations
            .Setup(q => q.GetCurrentForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quotation
            {
                Id = Guid.NewGuid(),
                ProjectId = ProjectId,
                EstimatedTotal = total,
                CreatedBy = Guid.NewGuid(),
                CreatedAtUtc = new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc)
            });

    /// <summary>The repository raises a new invoice for whatever it is asked.</summary>
    private void StubInvoiceRaised() =>
        _invoices
            .Setup(i => i.CreateFromEventIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid projectId, decimal amount, Guid raisedBy, Guid sourceEventId, CancellationToken _) =>
                new Invoice
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Amount = amount,
                    Status = InvoiceStatus.Pending,
                    CreatedBy = raisedBy,
                    SourceEventId = sourceEventId,
                    CreatedAtUtc = new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc)
                });

    private void StubCompletion(bool announced) =>
        _completions
            .Setup(c => c.RecordCompletionAndMaybeAnnounceAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(announced);

    private static string ConstructionStarted() => Envelope("ConstructionStarted", EventId, StartedPayload());

    private static string StartedPayload() =>
        $"{{\"projectId\":\"{ProjectId}\",\"startedAt\":\"{CompletedAt:O}\",\"milestoneCount\":7,\"startedBy\":\"{StartedBy}\"}}";

    private static string CompletedPayload() =>
        $"{{\"projectId\":\"{ProjectId}\",\"completedAt\":\"{CompletedAt:O}\"}}";

    private static string Envelope(string eventType, Guid eventId, string payloadJson) =>
        $"{{\"eventType\":\"{eventType}\",\"eventId\":\"{eventId}\","
        + $"\"occurredAt\":\"{CompletedAt:O}\",\"payload\":{payloadJson}}}";

    private Task HandleAsync(string? body)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_quotations.Object);
        services.AddSingleton(_invoices.Object);
        services.AddSingleton(_completions.Object);

        var consumer = new ConstructionEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<ConstructionEventsConsumer>.Instance);

        return consumer.HandleAsync(body, CancellationToken.None);
    }
}
