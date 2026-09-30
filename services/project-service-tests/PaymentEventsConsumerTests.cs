using System.Text.Json;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="PaymentEventsConsumer.HandleAsync"/> — what one message off <c>payment-events</c>
/// does to a project's payment status (US-24 AC-4).
/// </summary>
/// <remarks>
/// The Kafka plumbing around <c>HandleAsync</c> — subscribe, poll, commit, back off — is not
/// exercised here; what is pinned is the message-level decision and the exception contract
/// <c>ProcessAsync</c> maps to a commit.
/// </remarks>
public class PaymentEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 20, 11, 0, 0, TimeSpan.Zero);

    private readonly RecordingProjectRepository _projects = new();

    [Theory]
    [InlineData("Pending", ProjectPaymentStatus.PartiallyPaid)]
    [InlineData("Paid", ProjectPaymentStatus.InvoicePaid)]
    public async Task The_invoices_status_decides_the_reflected_payment_status(
        string invoiceStatus,
        ProjectPaymentStatus expected)
    {
        await Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId, invoiceStatus }),
            CancellationToken.None);

        var call = Assert.Single(_projects.PaymentStatusCalls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(expected, call.PaymentStatus);
    }

    [Fact]
    public async Task The_event_id_and_its_time_are_passed_through_for_deduplication()
    {
        // The repository deduplicates on the event id, and stamps the status with when the
        // payment happened rather than when this message was read — a consumer that was down for
        // an hour must not backdate the change to now.
        await Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId, invoiceStatus = "Paid" }),
            CancellationToken.None);

        var call = Assert.Single(_projects.PaymentStatusCalls);
        Assert.Equal(EventId, call.SourceEventId);
        Assert.Equal(OccurredAt.UtcDateTime, call.OccurredAtUtc);
    }

    [Fact]
    public async Task A_redelivery_the_repository_absorbs_is_not_an_error()
    {
        // Kafka delivers at least once. The repository answers false for an event it has already
        // applied, and that is a success — the consumer must commit past it rather than retry.
        _projects.UpdateResult = false;

        await Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId, invoiceStatus = "Paid" }),
            CancellationToken.None);

        Assert.Single(_projects.PaymentStatusCalls);
    }

    [Theory]
    [InlineData("InvoiceGenerated")]
    [InlineData("FinalPaymentSettled")]
    [InlineData("SomethingElseEntirely")]
    public async Task An_event_type_this_service_does_not_handle_is_left_alone(string eventType)
    {
        await Consumer().HandleAsync(
            Envelope(eventType, new { projectId = ProjectId, invoiceStatus = "Paid" }),
            CancellationToken.None);

        Assert.Empty(_projects.PaymentStatusCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json at all {")]
    [InlineData("{ \"eventType\": \"PaymentReceived\" ")]
    public async Task A_message_that_cannot_be_parsed_throws_JsonException(string body)
    {
        // ProcessAsync maps a JsonException to "commit past it": a malformed message will not
        // parse on a retry, and holding the partition on it would stop everything behind it.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(body, CancellationToken.None));
        Assert.Empty(_projects.PaymentStatusCalls);
    }

    [Fact]
    public async Task A_PaymentReceived_with_no_payload_object_throws_JsonException()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            eventType = "PaymentReceived",
            eventId = EventId,
            occurredAt = OccurredAt,
            payload = (object?)null
        });

        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(envelope, CancellationToken.None));
        Assert.Empty(_projects.PaymentStatusCalls);
    }

    [Fact]
    public async Task A_PaymentReceived_missing_its_project_id_throws_JsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("PaymentReceived", new { invoiceStatus = "Paid" }),
            CancellationToken.None));

        Assert.Empty(_projects.PaymentStatusCalls);
    }

    [Theory]
    [InlineData("Refunded")]
    [InlineData("")]
    [InlineData("paid")]
    public async Task An_invoice_status_this_service_does_not_know_is_refused_not_guessed(string invoiceStatus)
    {
        // Defaulting an unknown status would be worse than failing: to PartiallyPaid it would
        // understate a settled invoice, to InvoicePaid it would overstate an unpaid one. Note
        // "paid" in the wrong case is refused too — the wire value is the contract.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId, invoiceStatus }),
            CancellationToken.None));

        Assert.Empty(_projects.PaymentStatusCalls);
    }

    [Fact]
    public async Task A_repository_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        // ProcessAsync catches this, logs it, and does NOT commit — the message is re-read on the
        // next poll, and the update is deduplicated, so the retry is safe.
        _projects.UpdateThrows = new InvalidOperationException("project-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            Envelope("PaymentReceived", new { projectId = ProjectId, invoiceStatus = "Paid" }),
            CancellationToken.None));
    }

    private static string Envelope(string eventType, object payload) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId = EventId,
            occurredAt = OccurredAt,
            payload
        });

    private PaymentEventsConsumer Consumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IProjectRepository>(_ => _projects)
            .BuildServiceProvider();

        return new PaymentEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:29092",
                ConsumerGroupId = "project-service-tests"
            }),
            NullLogger<PaymentEventsConsumer>.Instance);
    }

    /// <summary>
    /// An <see cref="IProjectRepository"/> that records the payment-status updates asked of it.
    /// Only the one member this consumer touches does anything; the rest throw, so a change that
    /// starts using one cannot pass unnoticed.
    /// </summary>
    private sealed class RecordingProjectRepository : IProjectRepository
    {
        public bool UpdateResult { get; set; } = true;

        public Exception? UpdateThrows { get; set; }

        public List<PaymentStatusCall> PaymentStatusCalls { get; } = [];

        public Task<bool> UpdatePaymentStatusAsync(
            Guid projectId,
            ProjectPaymentStatus paymentStatus,
            Guid sourceEventId,
            DateTime occurredAtUtc)
        {
            if (UpdateThrows is not null)
            {
                throw UpdateThrows;
            }

            PaymentStatusCalls.Add(new PaymentStatusCall(projectId, paymentStatus, sourceEventId, occurredAtUtc));
            return Task.FromResult(UpdateResult);
        }

        public Task<Project?> GetByIdAsync(Guid id) => throw new NotSupportedException();

        public Task InsertAsync(
            Project project,
            ProjectStatusChange creation,
            IReadOnlyList<OutboxEvent> outboxEvents) => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListAllAsync() => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            throw new NotSupportedException();

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents) => throw new NotSupportedException();

        public Task<bool> AssignArchitectAsync(
            Guid projectId,
            Guid architectId,
            ProjectStatusChange? transition,
            IReadOnlyList<OutboxEvent> outboxEvents,
            DateTime updatedAtUtc) => throw new NotSupportedException();

        public Task<bool> AssignProjectManagerAsync(
            Guid projectId,
            Guid projectManagerId,
            DateTime updatedAtUtc) => throw new NotSupportedException();

        internal readonly record struct PaymentStatusCall(
            Guid ProjectId,
            ProjectPaymentStatus PaymentStatus,
            Guid SourceEventId,
            DateTime OccurredAtUtc);
    }
}
