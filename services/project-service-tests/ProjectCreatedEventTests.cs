using System.Text.Json;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The shape of what goes onto <c>project-events</c>.
/// </summary>
/// <remarks>
/// This is a contract with four other services, and it is a contract nothing
/// checks at compile time: a consumer reads JSON off a topic, so a renamed
/// property or a dropped field fails at runtime in somebody else's service
/// rather than here. These tests pin the envelope and payload as agreed. No
/// broker is needed — the envelope is built and serialised in-process.
/// </remarks>
public class ProjectCreatedEventTests
{
    private static readonly DateTime CreatedAt = new(2026, 8, 30, 9, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void The_service_publishes_to_one_topic_named_for_itself()
    {
        // One topic per publishing service, not one per event type.
        Assert.Equal("project-events", KafkaProjectEventPublisher.Topic);
    }

    [Fact]
    public void The_envelope_carries_exactly_the_four_agreed_properties()
    {
        var properties = Serialise().RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(["eventType", "eventId", "occurredAt", "payload"], properties);
    }

    [Fact]
    public void The_event_type_is_ProjectCreated()
    {
        Assert.Equal("ProjectCreated", Serialise().RootElement.GetProperty("eventType").GetString());
        Assert.Equal("ProjectCreated", ProjectEventTypes.ProjectCreated);
    }

    [Fact]
    public void The_event_id_is_a_guid()
    {
        Assert.True(Guid.TryParse(Serialise().RootElement.GetProperty("eventId").GetString(), out var eventId));
        Assert.NotEqual(Guid.Empty, eventId);
    }

    [Fact]
    public void The_event_id_identifies_the_publication_not_the_project()
    {
        // Two events about the same project must not share an id, or a consumer
        // deduplicating on it would drop the second one.
        var project = ProjectFor();

        var first = EventEnvelope<ProjectCreatedPayload>.Create(
            ProjectEventTypes.ProjectCreated, ProjectCreatedPayload.From(project), CreatedAt);
        var second = EventEnvelope<ProjectCreatedPayload>.Create(
            ProjectEventTypes.ProjectCreated, ProjectCreatedPayload.From(project), CreatedAt);

        Assert.NotEqual(first.EventId, second.EventId);
    }

    [Fact]
    public void The_time_it_happened_is_iso_8601()
    {
        var occurredAt = Serialise().RootElement.GetProperty("occurredAt").GetString();

        Assert.Equal("2026-08-30T09:15:00+00:00", occurredAt);
    }

    [Fact]
    public void The_payload_carries_the_whole_project()
    {
        // A consumer cannot query this service's database to fill in the rest —
        // one schema per service — so an id-only event would force a REST call
        // back here for every message.
        var payload = Serialise().RootElement.GetProperty("payload");

        Assert.Equal("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b", payload.GetProperty("projectId").GetString());
        Assert.Equal("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c", payload.GetProperty("clientId").GetString());
        Assert.Equal("Beachfront villa", payload.GetProperty("name").GetString());
        Assert.Equal("Galle", payload.GetProperty("location").GetString());
        Assert.Equal(25.5m, payload.GetProperty("landSizePerches").GetDecimal());
        Assert.Equal(18_500_000m, payload.GetProperty("budget").GetDecimal());
        Assert.Equal(2, payload.GetProperty("floors").GetInt32());
        Assert.Equal(4, payload.GetProperty("bedrooms").GetInt32());
        Assert.Equal(3, payload.GetProperty("bathrooms").GetInt32());
        Assert.Equal(2, payload.GetProperty("garageSpaces").GetInt32());
        Assert.Equal("Solar hot water", payload.GetProperty("otherRequirements").GetString());
    }

    [Fact]
    public void The_payload_carries_the_status_as_its_name()
    {
        // Not the enum's number: a consumer must not be tied to our ordering.
        Assert.Equal("Pending", Serialise().RootElement.GetProperty("payload").GetProperty("status").GetString());
    }

    [Fact]
    public void Absent_other_requirements_are_carried_as_null_rather_than_dropped()
    {
        // The key is always present, so a consumer reads the same set of fields
        // on every message.
        var payload = Serialise(ProjectFor(otherRequirements: null)).RootElement.GetProperty("payload");

        Assert.True(payload.TryGetProperty("otherRequirements", out var requirements));
        Assert.Equal(JsonValueKind.Null, requirements.ValueKind);
    }

    [Fact]
    public void The_payload_is_built_from_the_stored_project()
    {
        var payload = ProjectCreatedPayload.From(ProjectFor());

        Assert.Equal(Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b"), payload.ProjectId);
        Assert.Equal("Pending", payload.Status);
        Assert.Equal(CreatedAt, payload.CreatedAt);
    }

    private static JsonDocument Serialise(Project? project = null)
    {
        var envelope = EventEnvelope<ProjectCreatedPayload>.Create(
            ProjectEventTypes.ProjectCreated,
            ProjectCreatedPayload.From(project ?? ProjectFor()),
            CreatedAt);

        return JsonDocument.Parse(envelope.ToJson());
    }

    private static Project ProjectFor(string? otherRequirements = "Solar hot water") => new()
    {
        Id = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b"),
        ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c"),
        Name = "Beachfront villa",
        Location = "Galle",
        LandSizePerches = 25.5m,
        Budget = 18_500_000m,
        Floors = 2,
        Bedrooms = 4,
        Bathrooms = 3,
        GarageSpaces = 2,
        OtherRequirements = otherRequirements,
        Status = ProjectStatus.Pending,
        CreatedAt = CreatedAt,
        UpdatedAt = CreatedAt
    };
}
