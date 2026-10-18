using System.Text.Json;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// Kafka messages that can never be handled, sent to both consumers: each must be
/// reported as a <see cref="JsonException"/> and nothing else.
/// </summary>
/// <remarks>
/// Added for mutation testing (SE3112-04). The exception type decides what happens
/// to the message (see <see cref="ProjectEventsConsumer.HandleAsync"/>): a
/// JsonException is committed past, and any other exception is retried. A bad message
/// that throws the wrong type is therefore retried forever and blocks its partition.
/// <para>
/// Stryker showed that deleting several of the consumers' <c>throw new JsonException</c>
/// lines broke no test, because the existing inputs still ended in a JsonException a
/// line later. The inputs here are the ones where that is no longer true: a null body
/// (a Kafka "tombstone") and an envelope with no <c>payload</c> key at all.
/// </para>
/// </remarks>
public class MalformedEventMessageTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly Mock<IProjectOwnerRepository> _owners = new();
    private readonly Mock<IQuotationRepository> _quotations = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IConstructionCompletionRepository> _completions = new();

    // ----------------------------------------------- a null message body ----

    [Fact]
    public async Task ProjectEventsConsumer_reports_a_null_body_as_a_json_failure()
    {
        // Without its own check, a null body reaches JsonSerializer.Deserialize,
        // which throws ArgumentNullException, and the message would be retried forever.
        await Assert.ThrowsAsync<JsonException>(() => ProjectConsumer().HandleAsync(null, default));
    }

    [Fact]
    public async Task ConstructionEventsConsumer_reports_a_null_body_as_a_json_failure()
    {
        await Assert.ThrowsAsync<JsonException>(() => ConstructionConsumer().HandleAsync(null, default));
    }

    // ------------------------------------- an envelope with no payload key ----

    [Fact]
    public async Task A_ProjectCreated_with_no_payload_key_is_a_json_failure_and_records_no_owner()
    {
        await Assert.ThrowsAsync<JsonException>(
            () => ProjectConsumer().HandleAsync(EnvelopeWithoutPayload("ProjectCreated"), default));

        _owners.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_ConstructionStarted_with_no_payload_key_is_a_json_failure_and_bills_nobody()
    {
        await Assert.ThrowsAsync<JsonException>(
            () => ConstructionConsumer().HandleAsync(EnvelopeWithoutPayload("ConstructionStarted"), default));

        _quotations.VerifyNoOtherCalls();
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_ConstructionCompleted_with_no_payload_key_is_a_json_failure_and_records_nothing()
    {
        await Assert.ThrowsAsync<JsonException>(
            () => ConstructionConsumer().HandleAsync(EnvelopeWithoutPayload("ConstructionCompleted"), default));

        _completions.VerifyNoOtherCalls();
    }

    // --------------------------- the error names what was wrong ----

    // The consumer logs the exception and moves on, so its message is the only record
    // of why an event was skipped. These check that it names the missing field.

    [Fact]
    public async Task A_ProjectCreated_missing_its_client_says_which_ids_it_needs()
    {
        var message = Envelope("ProjectCreated", Guid.NewGuid(), $"{{\"projectId\":\"{ProjectId}\"}}");

        var failure = await Assert.ThrowsAsync<JsonException>(() => ProjectConsumer().HandleAsync(message, default));

        Assert.Contains("clientId", failure.Message);
    }

    [Fact]
    public async Task A_ConstructionStarted_missing_its_ids_says_which_ids_it_needs()
    {
        var message = Envelope("ConstructionStarted", Guid.NewGuid(), $"{{\"projectId\":\"{ProjectId}\"}}");

        var failure = await Assert.ThrowsAsync<JsonException>(
            () => ConstructionConsumer().HandleAsync(message, default));

        Assert.Contains("startedBy", failure.Message);
    }

    [Fact]
    public async Task A_ConstructionStarted_with_no_event_id_says_the_event_id_is_missing()
    {
        // The eventId is what stops a redelivered message billing the project twice.
        var message = Envelope(
            "ConstructionStarted",
            Guid.Empty,
            $"{{\"projectId\":\"{ProjectId}\",\"startedBy\":\"{UserId}\"}}");

        var failure = await Assert.ThrowsAsync<JsonException>(
            () => ConstructionConsumer().HandleAsync(message, default));

        Assert.Contains("eventId", failure.Message);
    }

    [Fact]
    public async Task A_ConstructionCompleted_missing_its_project_says_the_project_id_is_missing()
    {
        var message = Envelope(
            "ConstructionCompleted",
            Guid.NewGuid(),
            $"{{\"completedAt\":\"{DateTimeOffset.UtcNow:O}\"}}");

        var failure = await Assert.ThrowsAsync<JsonException>(
            () => ConstructionConsumer().HandleAsync(message, default));

        Assert.Contains("projectId", failure.Message);
    }

    // ---------------------------------------------------------- helpers ----

    private static string Envelope(string eventType, Guid eventId, string payloadJson) =>
        $"{{\"eventType\":\"{eventType}\",\"eventId\":\"{eventId}\","
        + $"\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":{payloadJson}}}";

    /// <summary>
    /// An envelope with no <c>payload</c> key. Different from <c>"payload": null</c>,
    /// which the existing tests already cover.
    /// </summary>
    private static string EnvelopeWithoutPayload(string eventType) =>
        $"{{\"eventType\":\"{eventType}\",\"eventId\":\"{Guid.NewGuid()}\","
        + $"\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\"}}";

    private ProjectEventsConsumer ProjectConsumer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_owners.Object);

        return new ProjectEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<ProjectEventsConsumer>.Instance);
    }

    private ConstructionEventsConsumer ConstructionConsumer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_quotations.Object);
        services.AddSingleton(_invoices.Object);
        services.AddSingleton(_completions.Object);

        return new ConstructionEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<ConstructionEventsConsumer>.Instance);
    }
}
