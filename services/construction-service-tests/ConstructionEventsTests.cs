using System.Reflection;
using System.Text.Json;
using BuildNexus.ConstructionService.Messaging;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="ConstructionEvents"/> on its own — which events a transition raises,
/// and what their envelopes say. No database and no broker: minting an event is a
/// pure function of the phase, and it is worth being able to check that without
/// either.
/// </summary>
public class ConstructionEventsTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");

    private static readonly DateTime StartedAt = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = new(2026, 9, 14, 16, 45, 0, DateTimeKind.Utc);

    [Fact]
    public void Started_raises_ConstructionStarted_about_the_project()
    {
        var outboxEvent = ConstructionEvents.Started(StartedPhase(), milestoneCount: 7, ActingPm);

        Assert.Equal(ConstructionEventTypes.ConstructionStarted, outboxEvent.EventType);
        Assert.Equal(ProjectId, outboxEvent.ProjectId);
        // occurredAt is the moment the build started, not "now".
        Assert.Equal(StartedAt, outboxEvent.OccurredAt);
        // Never sent yet, and never tried.
        Assert.Null(outboxEvent.PublishedAt);
        Assert.Equal(0, outboxEvent.AttemptCount);
    }

    [Fact]
    public void Started_names_the_Project_Manager_who_decided()
    {
        // The only place this fact is known. The Project Service records who caused
        // each status change, so without it the move it makes in reaction would have
        // to either invent an author or record none.
        using var document = JsonDocument.Parse(
            ConstructionEvents.Started(StartedPhase(), milestoneCount: 7, ActingPm).Envelope);

        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal(ActingPm, payload.GetProperty("startedBy").GetGuid());
        Assert.Equal(ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(7, payload.GetProperty("milestoneCount").GetInt32());
    }

    [Fact]
    public void The_row_and_its_envelope_share_one_event_id()
    {
        // This is what lets a message on the topic be traced back to the row that
        // produced it — and what lets a consumer recognise a redelivery.
        var outboxEvent = ConstructionEvents.Started(StartedPhase(), milestoneCount: 1, ActingPm);

        using var document = JsonDocument.Parse(outboxEvent.Envelope);

        Assert.Equal(outboxEvent.Id, document.RootElement.GetProperty("eventId").GetGuid());
        Assert.NotEqual(Guid.Empty, outboxEvent.Id);
    }

    [Fact]
    public void Two_raises_of_the_same_transition_get_different_event_ids()
    {
        // The id identifies the publication, not the project: two genuinely separate
        // announcements must not collide, or a consumer deduplicating on the id
        // would silently drop the second.
        var phase = StartedPhase();

        Assert.NotEqual(
            ConstructionEvents.Started(phase, 1, ActingPm).Id,
            ConstructionEvents.Started(phase, 1, ActingPm).Id);
    }

    [Fact]
    public void Completed_raises_ConstructionCompleted_stamped_at_the_completion()
    {
        var outboxEvent = ConstructionEvents.Completed(CompletedPhase(), milestoneCount: 7, ActingPm);

        Assert.Equal(ConstructionEventTypes.ConstructionCompleted, outboxEvent.EventType);
        // Taken off the phase rather than the clock, so the event's occurredAt and
        // the row's completed_at cannot disagree.
        Assert.Equal(CompletedAt, outboxEvent.OccurredAt);

        using var document = JsonDocument.Parse(outboxEvent.Envelope);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal(ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(7, payload.GetProperty("milestoneCount").GetInt32());
        Assert.Equal(ActingPm, payload.GetProperty("completedBy").GetGuid());
        Assert.Equal(StartedAt, payload.GetProperty("startedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(CompletedAt, payload.GetProperty("completedAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public void An_envelope_carries_exactly_the_four_agreed_properties()
    {
        // The envelope is a contract with the other services. An extra property is
        // as much a change as a missing one, so the whole set is asserted.
        var outboxEvent = ConstructionEvents.Started(StartedPhase(), milestoneCount: 1, ActingPm);

        using var document = JsonDocument.Parse(outboxEvent.Envelope);

        Assert.Equal(
            ["eventType", "eventId", "occurredAt", "payload"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void A_timestamp_read_back_without_a_Kind_still_goes_out_as_UTC()
    {
        // MySQL's DATETIME carries no offset, so a phase read out of the database
        // arrives Unspecified. Serialising that as-is would drop the Z and let a
        // consumer read the instant in its own timezone.
        var phase = new ConstructionPhase
        {
            ProjectId = ProjectId,
            Status = ConstructionPhaseStatus.Started,
            StartedAtUtc = DateTime.SpecifyKind(StartedAt, DateTimeKind.Unspecified),
            UpdatedAtUtc = DateTime.SpecifyKind(StartedAt, DateTimeKind.Unspecified)
        };

        using var document = JsonDocument.Parse(ConstructionEvents.Started(phase, 1, ActingPm).Envelope);

        Assert.Equal(
            TimeSpan.Zero,
            document.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Offset);
    }

    [Fact]
    public void The_database_allows_exactly_the_event_types_this_service_publishes()
    {
        // ConstructionEventTypes and ck_construction_outbox_events_type are two
        // copies of one list, and a new event type added to only one of them is
        // either an event the database silently refuses at the write or a type no
        // consumer agreed to. This holds the two together.
        var migration = ReadMigration("006_create_construction_outbox.sql");

        var allowed = migration
            .Split("CHECK (event_type IN (")[1]
            .Split("))")[0]
            .Split(',')
            .Select(value => value.Trim().Trim('\''))
            .ToList();

        Assert.Equal(ConstructionEventTypes.All.OrderBy(type => type), allowed.OrderBy(type => type));
    }

    private static ConstructionPhase StartedPhase() => new()
    {
        ProjectId = ProjectId,
        Status = ConstructionPhaseStatus.Started,
        StartedAtUtc = StartedAt,
        UpdatedAtUtc = StartedAt
    };

    private static ConstructionPhase CompletedPhase() => new()
    {
        ProjectId = ProjectId,
        Status = ConstructionPhaseStatus.Completed,
        StartedAtUtc = StartedAt,
        CompletedAtUtc = CompletedAt,
        UpdatedAtUtc = CompletedAt
    };

    /// <summary>
    /// Reads a migration out of the service assembly, where the csproj embeds them —
    /// the same copy DbUp applies, rather than a file path that would break the
    /// moment the tests run from a different directory.
    /// </summary>
    private static string ReadMigration(string fileName)
    {
        var assembly = typeof(ConstructionEventTypes).Assembly;

        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not read the embedded migration '{fileName}'.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
