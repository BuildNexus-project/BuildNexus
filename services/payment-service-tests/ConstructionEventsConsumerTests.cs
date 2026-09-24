using System.Text.Json;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// AC-2's automatic path: what
/// <see cref="ConstructionEventsConsumer.HandleAsync"/> does with one message
/// body.
/// </summary>
/// <remarks>
/// Drives the handler directly rather than through a broker — what is under test
/// is the decision made per message. The exception contract matters as much as
/// the happy path: a <see cref="JsonException"/> is what makes the caller commit
/// past a message that will never parse, and anything else is what makes it
/// leave the offset uncommitted to retry.
/// </remarks>
public class ConstructionEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StartedBy = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly FakeQuotationRepository _quotations = new();
    private readonly FakeInvoiceRepository _invoices = new();

    [Fact]
    public async Task A_ConstructionStarted_raises_an_invoice_for_the_projects_current_quotation()
    {
        // AC-2: a project reaching a billable stage is invoiced automatically,
        // for the amount it was most recently quoted.
        await _quotations.CreateAsync(ProjectId, 900_000m, Guid.NewGuid());
        await _quotations.CreateAsync(ProjectId, 1_100_000m, Guid.NewGuid());

        await HandleAsync(ConstructionStarted());

        var billed = Assert.Single(_invoices.Created);

        Assert.Equal(ProjectId, billed.ProjectId);
        Assert.Equal(1_100_000m, billed.Amount);
    }

    [Fact]
    public async Task The_invoice_is_attributed_to_the_project_manager_who_started_the_build()
    {
        // Nobody typed this invoice, but a real person's decision caused it — a
        // bill with no answerable author is worse than one attributed to them.
        await _quotations.CreateAsync(ProjectId, 500_000m, Guid.NewGuid());

        await HandleAsync(ConstructionStarted());

        Assert.Equal(StartedBy, Assert.Single(_invoices.Created).CreatedBy);
    }

    [Fact]
    public async Task A_redelivered_ConstructionStarted_does_not_bill_the_project_twice()
    {
        // The whole reason the causing event is recorded. Kafka delivers at least
        // once, and billing a client twice is not a defect you can clean up later.
        await _quotations.CreateAsync(ProjectId, 750_000m, Guid.NewGuid());
        var message = ConstructionStarted();

        await HandleAsync(message);
        await HandleAsync(message);

        Assert.Single(_invoices.Created);
    }

    [Fact]
    public async Task A_project_with_no_quotation_is_not_billed()
    {
        // An invoice needs an amount, and the event carries none. Guessing one
        // would be inventing a commercial rule; raising none leaves a Project
        // Manager free to bill by hand once the project is quoted.
        await HandleAsync(ConstructionStarted());

        Assert.Empty(_invoices.Created);
    }

    [Fact]
    public async Task A_project_with_no_quotation_is_skipped_rather_than_retried_forever()
    {
        // Skipped, not thrown: retrying would not conjure a quotation, and
        // holding the partition on it would stop every later event for no gain.
        var failure = await Record.ExceptionAsync(() => HandleAsync(ConstructionStarted()));

        Assert.Null(failure);
    }

    [Fact]
    public async Task ConstructionCompleted_does_not_raise_an_invoice()
    {
        // Only the start of the build is a billable stage in this story.
        await _quotations.CreateAsync(ProjectId, 500_000m, Guid.NewGuid());

        await HandleAsync(Envelope("ConstructionCompleted", new { projectId = ProjectId, startedBy = StartedBy }));

        Assert.Empty(_invoices.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task A_message_that_will_never_parse_is_reported_as_a_json_failure(string body)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(body));
    }

    [Theory]
    [InlineData("{\"startedBy\":\"bbbbbbbb-0000-4000-8000-000000000001\"}")]
    [InlineData("{\"projectId\":\"aaaaaaaa-0000-4000-8000-000000000001\"}")]
    [InlineData("{}")]
    public async Task A_ConstructionStarted_missing_either_id_is_reported_as_a_json_failure(string payload)
    {
        await _quotations.CreateAsync(ProjectId, 500_000m, Guid.NewGuid());

        var message = $"{{\"eventType\":\"ConstructionStarted\",\"eventId\":\"{Guid.NewGuid()}\","
                      + $"\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":{payload}}}";

        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(message));
        Assert.Empty(_invoices.Created);
    }

    [Fact]
    public async Task An_envelope_with_no_event_id_is_refused_rather_than_billed_without_a_guard()
    {
        // The eventId is what makes a redelivery a no-op. Billing without it
        // would bill again on every retry.
        await _quotations.CreateAsync(ProjectId, 500_000m, Guid.NewGuid());

        var message = "{\"eventType\":\"ConstructionStarted\",\"eventId\":\"00000000-0000-0000-0000-000000000000\","
                      + $"\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\","
                      + $"\"payload\":{{\"projectId\":\"{ProjectId}\",\"startedBy\":\"{StartedBy}\"}}}}";

        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(message));
        Assert.Empty(_invoices.Created);
    }

    [Fact]
    public async Task A_failing_write_surfaces_as_something_other_than_a_json_failure()
    {
        // The distinction the caller's commit decision turns on: a database that
        // is down is transient, so the offset must be left uncommitted and the
        // message retried — not committed past like unparseable input.
        await _quotations.CreateAsync(ProjectId, 500_000m, Guid.NewGuid());
        _invoices.NextWriteThrows = new InvalidOperationException("payment-db is unreachable.");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => HandleAsync(ConstructionStarted()));

        Assert.IsNotType<JsonException>(failure);
    }

    private static string ConstructionStarted() =>
        Envelope("ConstructionStarted", new
        {
            projectId = ProjectId,
            startedAt = DateTimeOffset.UtcNow,
            milestoneCount = 7,
            startedBy = StartedBy
        });

    private static string Envelope(string eventType, object payload) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            payload
        });

    private Task HandleAsync(string? body)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IQuotationRepository>(_quotations);
        services.AddSingleton<IInvoiceRepository>(_invoices);

        var consumer = new ConstructionEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<ConstructionEventsConsumer>.Instance);

        return consumer.HandleAsync(body, CancellationToken.None);
    }
}
