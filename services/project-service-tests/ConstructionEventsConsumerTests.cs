using System.Text.Json;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="ConstructionEventsConsumer.HandleAsync"/> — what one message off
/// <c>construction-events</c> does to a project's status (US-14 AC-1 and AC-4's
/// terminal move).
/// </summary>
/// <remarks>
/// The interesting behaviour here is everything that is <em>not</em> the happy path:
/// this service will not bend its own lifecycle rules because a request arrived over
/// a topic, and Kafka's at-least-once delivery means a redelivered move has to be
/// absorbed rather than applied twice. Those two together are most of this file.
/// <para>
/// The Kafka plumbing around <c>HandleAsync</c> — subscribe, poll, commit, back off
/// — is not exercised here; what is pinned is the message-level decision and the
/// exception contract <c>ProcessAsync</c> maps to a commit.
/// </para>
/// </remarks>
public class ConstructionEventsConsumerTests
{
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly Guid ClientId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
    private static readonly Guid ActingPm = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly Guid EventId = Guid.Parse("20e2f4c5-6ea1-4bb1-aef7-18b9a6ba6bd5");
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly RecordingProjectRepository _projects = new();

    // ------------------------------------------------ ConstructionStarted ----

    [Fact]
    public async Task ConstructionStarted_moves_a_DesignApproved_project_to_Construction()
    {
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        var change = Assert.Single(_projects.StatusChanges).Change;
        Assert.Equal(ProjectStatus.DesignApproved, change.FromStatus);
        Assert.Equal(ProjectStatus.Construction, change.ToStatus);
    }

    [Fact]
    public async Task The_status_change_is_attributed_to_the_Project_Manager_the_event_names()
    {
        // The whole reason the Construction Service puts the actor on the event: an
        // audit trail that said "nobody" or invented an author would be worse than
        // no trail at all.
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        var change = Assert.Single(_projects.StatusChanges).Change;
        Assert.Equal(ActingPm, change.ChangedByUserId);
        Assert.Equal(PlatformRoles.ProjectManager, change.ChangedByRole);
        // And the role is one the platform actually has, so the frontend's role
        // labels can render it.
        Assert.Contains(change.ChangedByRole, PlatformRoles.All);
    }

    [Fact]
    public async Task The_change_is_stamped_when_the_build_moved_not_when_the_message_was_read()
    {
        // A consumer that was down for an hour must not backdate the history to the
        // moment it caught up — occurredAt is when the transition actually happened.
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        var recorded = Assert.Single(_projects.StatusChanges);
        Assert.Equal(OccurredAt.UtcDateTime, recorded.Change.ChangedAt);
        Assert.Equal(OccurredAt.UtcDateTime, recorded.UpdatedAtUtc);
    }

    [Fact]
    public async Task The_move_announces_a_ProjectUpdated_in_the_same_write()
    {
        // A Kafka-driven transition is a transition: it goes through the same
        // repository call as the HTTP one, so it gets a history row and its own
        // event rather than moving the column silently.
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        var recorded = Assert.Single(_projects.StatusChanges);
        Assert.Contains(recorded.OutboxEvents, e => e.EventType == ProjectEventTypes.ProjectUpdated);
        Assert.All(recorded.OutboxEvents, e => Assert.Equal(ProjectId, e.ProjectId));
    }

    // ---------------------------------------------- ConstructionCompleted ----

    [Fact]
    public async Task ConstructionCompleted_moves_a_Construction_project_to_Completed()
    {
        _projects.Project = ProjectAt(ProjectStatus.Construction);

        await Consumer().HandleAsync(CompletedEnvelope(), CancellationToken.None);

        var change = Assert.Single(_projects.StatusChanges).Change;
        Assert.Equal(ProjectStatus.Construction, change.FromStatus);
        Assert.Equal(ProjectStatus.Completed, change.ToStatus);
        // The actor's field is named differently on this payload, and is read from it.
        Assert.Equal(ActingPm, change.ChangedByUserId);
    }

    // -------------------------------------------------------- refusals ------

    [Fact]
    public async Task A_project_already_in_the_target_status_is_absorbed()
    {
        // Kafka delivers at least once and the outbox dispatcher re-sends anything it
        // could not confirm. A second delivery of a move already made is a success,
        // not a conflict — and must not write a second history row saying the project
        // moved to where it already was.
        _projects.Project = ProjectAt(ProjectStatus.Construction);

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        Assert.Empty(_projects.StatusChanges);
    }

    [Theory]
    [InlineData(ProjectStatus.Pending)]
    [InlineData(ProjectStatus.Designing)]
    [InlineData(ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Cancelled)]
    public async Task ConstructionStarted_is_ignored_when_the_lifecycle_forbids_the_move(
        ProjectStatus current)
    {
        // The transition table is the same rule the HTTP endpoint enforces, and it is
        // not relaxed for a message. A cancelled project especially must not be
        // dragged back into Construction by an event.
        _projects.Project = ProjectAt(current);

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        Assert.Empty(_projects.StatusChanges);
    }

    [Fact]
    public async Task ConstructionCompleted_is_ignored_when_the_build_never_started_here()
    {
        // The Started event was lost or has not been read yet. Completing from
        // DesignApproved would skip a stage the history is supposed to record, so the
        // move is refused and the project waits for the earlier event.
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);

        await Consumer().HandleAsync(CompletedEnvelope(), CancellationToken.None);

        Assert.Empty(_projects.StatusChanges);
    }

    [Fact]
    public async Task An_event_about_an_unknown_project_is_skipped_rather_than_retried()
    {
        // This service owns projects, so this cannot be a race — the project does not
        // exist and never will. Committing past it keeps the partition moving.
        _projects.Project = null;

        await Consumer().HandleAsync(StartedEnvelope(), CancellationToken.None);

        Assert.Empty(_projects.StatusChanges);
    }

    [Theory]
    [InlineData("ProjectCreated")]
    [InlineData("DesignApproved")]
    [InlineData("SomethingElseEntirely")]
    public async Task An_event_type_this_service_does_not_handle_is_left_alone(string eventType)
    {
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);

        await Consumer().HandleAsync(
            Envelope(eventType, new { projectId = ProjectId, startedBy = ActingPm }),
            CancellationToken.None);

        Assert.Empty(_projects.StatusChanges);
        // Not even read: the event type is checked before the project is fetched.
        Assert.Empty(_projects.GetByIdCalls);
    }

    // ------------------------------------------------- malformed input ------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json at all {")]
    [InlineData("{ \"eventType\": \"ConstructionStarted\" ")]
    public async Task A_message_that_cannot_be_parsed_throws_JsonException(string body)
    {
        // ProcessAsync maps a JsonException to "commit past it": a malformed message
        // will not parse on a retry, and holding the partition on it would stop
        // everything behind it.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(body, CancellationToken.None));
        Assert.Empty(_projects.StatusChanges);
    }

    [Fact]
    public async Task A_ConstructionStarted_with_no_payload_object_throws_JsonException()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            eventType = "ConstructionStarted",
            eventId = EventId,
            occurredAt = OccurredAt,
            payload = (object?)null
        });

        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(envelope, CancellationToken.None));
        Assert.Empty(_projects.StatusChanges);
    }

    [Fact]
    public async Task A_ConstructionStarted_missing_its_project_id_throws_JsonException()
    {
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("ConstructionStarted", new { startedBy = ActingPm }),
            CancellationToken.None));

        Assert.Empty(_projects.StatusChanges);
    }

    [Fact]
    public async Task A_ConstructionStarted_with_no_actor_throws_JsonException()
    {
        // Rather than record the move with no author. An audit trail with an invented
        // name in it is worse than a message that has to be looked at by a human.
        await Assert.ThrowsAsync<JsonException>(() => Consumer().HandleAsync(
            Envelope("ConstructionStarted", new { projectId = ProjectId }),
            CancellationToken.None));

        Assert.Empty(_projects.StatusChanges);
    }

    // --------------------------------------------------- transient fail -----

    [Fact]
    public async Task A_repository_failure_propagates_so_the_offset_is_left_uncommitted()
    {
        // ProcessAsync catches this, logs it, and does NOT commit — the message is
        // re-read and retried on the next poll, and the consume loop stays up.
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);
        _projects.UpdateThrows = new InvalidOperationException("project-db is unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            StartedEnvelope(), CancellationToken.None));
    }

    [Fact]
    public async Task A_project_that_moved_underneath_the_event_is_retried()
    {
        // The conditional update refused: somebody changed the status between the
        // read and the write. Throwing leaves the offset uncommitted, and the re-read
        // sees the new status — either as "already there" or as a forbidden move.
        _projects.Project = ProjectAt(ProjectStatus.DesignApproved);
        _projects.UpdateResult = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Consumer().HandleAsync(
            StartedEnvelope(), CancellationToken.None));
    }

    // ------------------------------------------------------- helpers --------

    private string StartedEnvelope() =>
        Envelope("ConstructionStarted", new
        {
            projectId = ProjectId,
            startedAt = OccurredAt,
            milestoneCount = 7,
            startedBy = ActingPm
        });

    private string CompletedEnvelope() =>
        Envelope("ConstructionCompleted", new
        {
            projectId = ProjectId,
            startedAt = OccurredAt,
            completedAt = OccurredAt,
            milestoneCount = 7,
            completedBy = ActingPm
        });

    private string Envelope(string eventType, object payload) =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId = EventId,
            occurredAt = OccurredAt,
            payload
        });

    private static Project ProjectAt(ProjectStatus status) => new()
    {
        Id = ProjectId,
        ClientId = ClientId,
        Name = "Beachfront villa",
        Location = "Galle",
        LandSizePerches = 25.5m,
        Budget = 18_500_000m,
        Floors = 3,
        Bedrooms = 4,
        Bathrooms = 5,
        GarageSpaces = 2,
        Status = status,
        CreatedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    private ConstructionEventsConsumer Consumer()
    {
        var provider = new ServiceCollection()
            .AddScoped<IProjectRepository>(_ => _projects)
            .BuildServiceProvider();

        return new ConstructionEventsConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:29092",
                ConsumerGroupId = "project-service-tests"
            }),
            NullLogger<ConstructionEventsConsumer>.Instance);
    }

    /// <summary>
    /// An <see cref="IProjectRepository"/> that records the status moves asked of it.
    /// Only the two members this consumer touches do anything; the rest are not part
    /// of its story and throw if called, so a change that starts using one cannot
    /// pass unnoticed.
    /// </summary>
    private sealed class RecordingProjectRepository : IProjectRepository
    {
        public Project? Project { get; set; }

        public bool UpdateResult { get; set; } = true;

        public Exception? UpdateThrows { get; set; }

        public List<Guid> GetByIdCalls { get; } = [];

        public List<RecordedStatusChange> StatusChanges { get; } = [];

        public Task<Project?> GetByIdAsync(Guid id)
        {
            GetByIdCalls.Add(id);
            return Task.FromResult(Project);
        }

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents)
        {
            if (UpdateThrows is not null)
            {
                throw UpdateThrows;
            }

            StatusChanges.Add(new RecordedStatusChange(change, updatedAtUtc, outboxEvents));
            return Task.FromResult(UpdateResult);
        }

        public Task InsertAsync(
            Project project,
            ProjectStatusChange creation,
            IReadOnlyList<OutboxEvent> outboxEvents) => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListAllAsync() => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            throw new NotSupportedException();

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

        internal readonly record struct RecordedStatusChange(
            ProjectStatusChange Change,
            DateTime UpdatedAtUtc,
            IReadOnlyList<OutboxEvent> OutboxEvents);
    }
}
