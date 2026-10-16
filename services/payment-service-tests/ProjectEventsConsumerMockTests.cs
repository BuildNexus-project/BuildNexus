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
/// What <see cref="ProjectEventsConsumer.HandleAsync"/> asks of the project-owner
/// repository for each message body, checked with a Moq mock (US-15).
/// </summary>
/// <remarks>
/// The older <see cref="ProjectEventsConsumerTests"/> check the owner ends up
/// recorded in a fake. These check the call itself — once, with the project and
/// client from the message — and that no message which is not a valid
/// <c>ProjectCreated</c> ever reaches the repository.
/// </remarks>
public class ProjectEventsConsumerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 31, 14, 5, 0, TimeSpan.Zero);

    private readonly Mock<IProjectOwnerRepository> _owners = new();

    public static TheoryData<string> CreatedPayloadsMissingAnId => new()
    {
        "{}",
        $"{{\"projectId\":\"{ProjectId}\"}}",
        $"{{\"clientId\":\"{ClientId}\"}}",
        $"{{\"projectId\":\"{Guid.Empty}\",\"clientId\":\"{ClientId}\"}}",
        $"{{\"projectId\":\"{ProjectId}\",\"clientId\":\"{Guid.Empty}\"}}"
    };

    [Fact]
    public async Task A_ProjectCreated_records_the_owner_once_with_the_ids_from_the_message()
    {
        StubRecordOwner(written: true);

        await HandleAsync(ProjectCreated());

        _owners.Verify(
            o => o.RecordOwnerIfAbsentAsync(ProjectId, ClientId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_ProjectCreated_for_a_project_that_already_has_an_owner_is_absorbed_not_an_error()
    {
        // Duplicate event: the repository reports that nothing new was written.
        StubRecordOwner(written: false);

        var failure = await Record.ExceptionAsync(() => HandleAsync(ProjectCreated()));

        Assert.Null(failure);
        _owners.Verify(
            o => o.RecordOwnerIfAbsentAsync(ProjectId, ClientId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_asks_the_repository_twice_with_the_same_ids()
    {
        // Telling a repeat from a first delivery is the repository's job (its
        // primary key). The consumer's part is to pass the same ids both times.
        StubRecordOwner(written: true);
        var message = ProjectCreated();

        await HandleAsync(message);
        await HandleAsync(message);

        _owners.Verify(
            o => o.RecordOwnerIfAbsentAsync(ProjectId, ClientId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData("ProjectUpdated")]
    [InlineData("ProjectApproved")]
    [InlineData("ConstructionStarted")]
    [InlineData("projectcreated")]
    public async Task Any_other_event_type_is_ignored_and_the_repository_is_never_called(string eventType)
    {
        // The last one is the right name in the wrong case: the comparison is
        // exact, so it is ignored rather than recorded.
        await HandleAsync(Envelope(eventType, CreatedPayload()));

        _owners.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(CreatedPayloadsMissingAnId))]
    public async Task A_ProjectCreated_missing_or_zeroing_an_id_is_a_json_failure_and_records_nothing(
        string payload)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(Envelope("ProjectCreated", payload)));

        _owners.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task A_body_that_will_never_parse_is_a_json_failure_and_records_nothing(string body)
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(body));

        _owners.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_ProjectCreated_whose_payload_is_not_an_object_is_a_json_failure_and_records_nothing()
    {
        await Assert.ThrowsAsync<JsonException>(() => HandleAsync(Envelope("ProjectCreated", "\"just text\"")));

        _owners.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failing_owner_write_surfaces_as_something_other_than_a_json_failure()
    {
        // So the caller leaves the offset uncommitted and the message is retried.
        _owners
            .Setup(o => o.RecordOwnerIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("payment-db is unreachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(ProjectCreated()));
    }

    // -------------------------------------------------------------- helpers ----

    private void StubRecordOwner(bool written) =>
        _owners
            .Setup(o => o.RecordOwnerIfAbsentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(written);

    private static string ProjectCreated() => Envelope("ProjectCreated", CreatedPayload());

    private static string CreatedPayload() =>
        $"{{\"projectId\":\"{ProjectId}\",\"clientId\":\"{ClientId}\"}}";

    private static string Envelope(string eventType, string payloadJson) =>
        $"{{\"eventType\":\"{eventType}\",\"eventId\":\"{Guid.NewGuid()}\","
        + $"\"occurredAt\":\"{OccurredAt:O}\",\"payload\":{payloadJson}}}";

    private Task HandleAsync(string? body)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_owners.Object);

        var consumer = new ProjectEventsConsumer(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = "localhost:29092" }),
            NullLogger<ProjectEventsConsumer>.Instance);

        return consumer.HandleAsync(body, CancellationToken.None);
    }
}
