using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The parts of <see cref="BuildNexus.ProjectService.Data.ProjectRepository"/>
/// that only the real database can answer, run against real MySQL.
/// </summary>
/// <remarks>
/// The status-history ordering defect this fixes was invisible to every
/// stub-based test in this project, and would have stayed invisible: an
/// in-memory list sorts on whatever precision a <c>DateTime</c> carries, while
/// MySQL's <c>DATETIME</c> holds whole seconds and throws the rest away. The
/// collision only exists once the value has been through the column.
/// <para>
/// Needs <c>project-db</c> running — see <see cref="ProjectDatabaseFixture"/>.
/// </para>
/// </remarks>
[Collection(ProjectDatabaseCollection.Name)]
public class ProjectRepositoryDatabaseTests
{
    /// <summary>
    /// One second, shared by every change in a test. Whole seconds on purpose:
    /// it is what the column stores, so writing anything finer would only be
    /// truncated on the way in and prove nothing.
    /// </summary>
    private static readonly DateTime SameSecond = new(2026, 8, 31, 3, 23, 23, DateTimeKind.Utc);

    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid ProjectManagerId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly ProjectDatabaseFixture _fixture;

    /// <summary>
    /// Four hex characters unique to this test. xUnit builds the class once per
    /// test method, so each gets its own — which keeps the ids below unique in
    /// a database every test in the collection shares.
    /// </summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..4];

    public ProjectRepositoryDatabaseTests(ProjectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // Ids whose leading characters make sorting on them not merely unhelpful
    // but actively wrong: the creation sorts last and the transitions sort out
    // of sequence, so the old `ORDER BY changed_at, id` returned them as
    // first move, third move, second move, creation.
    //
    // Pinned rather than random, because a random id would make this test pass
    // roughly half the time — worse than no test at all. Only the first segment
    // decides the order, so the per-test discriminator below cannot disturb it.
    private Guid CreationId => ChangeId("ffffffff");

    private Guid FirstMoveId => ChangeId("00000000");

    private Guid SecondMoveId => ChangeId("eeeeeeee");

    private Guid ThirdMoveId => ChangeId("11111111");

    private Guid ChangeId(string sortsAs) => Guid.Parse($"{sortsAs}-{_run}-4000-8000-000000000001");

    [Fact]
    public async Task Returns_a_creation_and_a_same_second_transition_in_the_order_they_happened()
    {
        // The regression. A project created and moved on within the same second
        // is not a contrived case — it is a demo, a smoke test, or somebody
        // clicking through a review quickly.
        var project = await CreateProjectAsync();

        await MoveAsync(project, FirstMoveId, ProjectStatus.Pending, ProjectStatus.Designing);

        var history = await _fixture.Repository.GetStatusHistoryAsync(project.Id);

        Assert.Equal(2, history.Count);
        // The creation first, though its id sorts after the transition's and
        // both rows carry the same timestamp.
        Assert.Null(history[0].FromStatus);
        Assert.Equal(ProjectStatus.Pending, history[0].ToStatus);
        Assert.Equal(ProjectStatus.Pending, history[1].FromStatus);
        Assert.Equal(ProjectStatus.Designing, history[1].ToStatus);
    }

    [Fact]
    public async Task Keeps_a_whole_run_of_same_second_changes_in_order()
    {
        // Four changes, one second, ids deliberately scrambled. Ordering that
        // depends on the clock has nothing left to work with here.
        var project = await CreateProjectAsync();

        await MoveAsync(project, FirstMoveId, ProjectStatus.Pending, ProjectStatus.Designing);
        await MoveAsync(project, SecondMoveId, ProjectStatus.Designing, ProjectStatus.DesignApproved);
        await MoveAsync(project, ThirdMoveId, ProjectStatus.DesignApproved, ProjectStatus.Construction);

        var history = await _fixture.Repository.GetStatusHistoryAsync(project.Id);

        Assert.Equal(
            [ProjectStatus.Pending, ProjectStatus.Designing, ProjectStatus.DesignApproved, ProjectStatus.Construction],
            history.Select(change => change.ToStatus));

        // Every row landed in the same second, so the order above came from the
        // sequence and not from the timestamps.
        Assert.All(history, change => Assert.Equal(SameSecond, change.ChangedAt));
    }

    [Fact]
    public async Task Stores_the_opening_history_entry_with_the_project()
    {
        // Both rows or neither, in one transaction — and read back through the
        // real SELECT, so the column mapping is exercised too.
        var project = await CreateProjectAsync();

        var stored = await _fixture.Repository.GetByIdAsync(project.Id);

        Assert.NotNull(stored);
        Assert.Equal(project.Name, stored.Name);
        Assert.Equal(ProjectStatus.Pending, stored.Status);
        // Nothing assigns staff yet, so a freshly created project has nobody on
        // it — and the nullable columns must read back as null, not as an empty
        // Guid.
        Assert.Null(stored.AssignedArchitectId);
        Assert.Null(stored.AssignedProjectManagerId);

        var opening = Assert.Single(await _fixture.Repository.GetStatusHistoryAsync(project.Id));
        Assert.Null(opening.FromStatus);
        Assert.Equal(ClientId, opening.ChangedByUserId);
        Assert.Equal(PlatformRoles.Client, opening.ChangedByRole);
    }

    [Fact]
    public async Task Refuses_a_second_move_from_a_status_the_project_has_already_left()
    {
        // The optimistic-concurrency guard, against the real UPDATE rather than
        // a stand-in that simply returns false when told to. Two callers both
        // read Designing, both decide their move is valid; only one may win.
        var project = await CreateProjectAsync();
        await MoveAsync(project, FirstMoveId, ProjectStatus.Pending, ProjectStatus.Designing);

        var stale = Change(project, SecondMoveId, ProjectStatus.Pending, ProjectStatus.Designing);
        var accepted = await _fixture.Repository.UpdateStatusAsync(stale, SameSecond, []);

        Assert.False(accepted);

        // And nothing was written: no phantom history entry for the move that
        // did not happen.
        var history = await _fixture.Repository.GetStatusHistoryAsync(project.Id);
        Assert.Equal(2, history.Count);
    }

    /// <summary>
    /// A project stored through the real INSERT, named so the fixture's cleanup
    /// will find it.
    /// </summary>
    /// <param name="raises">
    /// The event the insert should carry, or <c>null</c> for the tests that only
    /// care about the project and its history.
    /// </param>
    private async Task<Project> CreateProjectAsync(Func<Project, OutboxEvent>? raises = null)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = ClientId,
            Name = $"{ProjectDatabaseFixture.TestProjectPrefix}{Guid.NewGuid()}",
            Location = "Galle",
            LandSizePerches = 25.5m,
            Budget = 18_500_000m,
            Floors = 2,
            Bedrooms = 4,
            Bathrooms = 3,
            GarageSpaces = 2,
            OtherRequirements = null,
            Status = ProjectStatus.Pending,
            CreatedAt = SameSecond,
            UpdatedAt = SameSecond
        };

        var creation = ProjectStatusChange.ForCreation(project, PlatformRoles.Client);
        // Pinned rather than left random, so the ordering under test is the one
        // that used to fail rather than a coin toss.
        creation.Id = CreationId;

        await _fixture.Repository.InsertAsync(
            project,
            creation,
            raises is null ? [] : [raises(project)]);

        return project;
    }

    // ------------------------------------------------- the outbox (US-22) ----

    [Fact]
    public async Task Stores_the_events_a_project_raises_with_the_project()
    {
        // The first US-22 acceptance bullet, against the real engine: the event
        // and the row it announces commit together. A stub cannot show this —
        // it has no transaction to roll back.
        var project = await CreateProjectAsync(ProjectEvents.Created);

        var stored = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));

        Assert.Equal(ProjectEventTypes.ProjectCreated, stored.EventType);
        Assert.Equal(project.Id, stored.ProjectId);
        Assert.False(stored.IsPublished);
        Assert.Equal(0, stored.AttemptCount);
        Assert.Null(stored.LastError);
    }

    [Fact]
    public async Task Keeps_the_envelope_byte_for_byte_through_the_column()
    {
        // LONGTEXT and not MySQL's JSON type, which would normalise the value
        // and reorder its keys — so a retry would put different bytes on the
        // topic than the first attempt did, under a different eventId.
        var project = await CreateProjectAsync(ProjectEvents.Created);
        var raised = ProjectEvents.Created(project);

        var stored = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));

        // The ids differ — each call mints its own — so compare the shape the
        // column has to preserve rather than the whole string.
        Assert.StartsWith("{\"eventType\":\"ProjectCreated\",\"eventId\":", stored.Envelope, StringComparison.Ordinal);
        Assert.Equal(raised.Envelope.Length, stored.Envelope.Length);
        Assert.Contains("\"occurredAt\":", stored.Envelope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stores_the_events_a_status_change_raises_with_the_move()
    {
        var project = await CreateProjectAsync();

        await MoveAsync(project, FirstMoveId, ProjectStatus.Pending, ProjectStatus.Designing);

        var stored = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));
        Assert.Equal(ProjectEventTypes.ProjectUpdated, stored.EventType);
    }

    [Fact]
    public async Task Numbers_two_events_from_one_move_in_the_order_they_go_on_the_topic()
    {
        // Both rows are written in one transaction and share occurred_at to the
        // second, so only sequence_number can tell them apart. The approval must
        // not overtake the update that carried the same move.
        var project = await CreateProjectAsync();

        await MoveAsync(project, FirstMoveId, ProjectStatus.Pending, ProjectStatus.Designing);
        await MoveAsync(project, SecondMoveId, ProjectStatus.Designing, ProjectStatus.DesignApproved);

        var stored = await _fixture.Outbox.ListForProjectAsync(project.Id);

        Assert.Equal(
            [
                ProjectEventTypes.ProjectUpdated,
                ProjectEventTypes.ProjectUpdated,
                ProjectEventTypes.ProjectApproved
            ],
            stored.Select(e => e.EventType));

        Assert.All(stored, e => Assert.Equal(SameSecond, e.OccurredAt));
        Assert.Equal(stored.Select(e => e.SequenceNumber).Order(), stored.Select(e => e.SequenceNumber));
    }

    [Fact]
    public async Task A_move_that_lost_a_race_leaves_no_event_behind()
    {
        // The guarded UPDATE matched nothing, so the whole transaction rolled
        // back — the event included. Announcing a move that did not happen is
        // the failure this ordering exists to prevent.
        var project = await CreateProjectAsync();

        await MoveAsync(project, FirstMoveId, ProjectStatus.Pending, ProjectStatus.Designing);

        var before = (await _fixture.Outbox.ListForProjectAsync(project.Id)).Count;

        // Stale: the project has already left Pending.
        var stale = Change(project, SecondMoveId, ProjectStatus.Pending, ProjectStatus.Designing);
        Assert.False(
            await _fixture.Repository.UpdateStatusAsync(
                stale, SameSecond, ProjectEvents.ForStatusChange(project, stale)));

        Assert.Equal(before, (await _fixture.Outbox.ListForProjectAsync(project.Id)).Count);
    }

    [Fact]
    public async Task Marks_an_event_delivered_and_clears_what_an_earlier_attempt_recorded()
    {
        var project = await CreateProjectAsync(ProjectEvents.Created);
        var stored = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));

        await _fixture.Outbox.MarkFailedAsync(stored.Id, "Local: Message timed out");

        var failed = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));
        Assert.False(failed.IsPublished);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal("Local: Message timed out", failed.LastError);

        await _fixture.Outbox.MarkPublishedAsync(stored.Id, SameSecond);

        var delivered = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));
        Assert.True(delivered.IsPublished);
        Assert.Equal(2, delivered.AttemptCount);
        // Cleared: it described an attempt that has since been superseded.
        Assert.Null(delivered.LastError);
    }

    [Fact]
    public async Task Offers_only_the_events_still_waiting_to_be_sent()
    {
        // What the dispatcher asks for. A delivered event must not come back.
        var project = await CreateProjectAsync(ProjectEvents.Created);
        var stored = Assert.Single(await _fixture.Outbox.ListForProjectAsync(project.Id));

        Assert.Contains(await _fixture.Outbox.ListPendingAsync(100), e => e.Id == stored.Id);

        await _fixture.Outbox.MarkPublishedAsync(stored.Id, SameSecond);

        Assert.DoesNotContain(await _fixture.Outbox.ListPendingAsync(100), e => e.Id == stored.Id);
    }

    private async Task MoveAsync(Project project, Guid changeId, ProjectStatus from, ProjectStatus to)
    {
        Assert.True(
            await MoveWithEventsAsync(project, changeId, from, to),
            $"The move from {from} to {to} should have been accepted.");
    }

    /// <summary>
    /// Moves the project, carrying the events the move actually raises rather
    /// than an empty list — so the outbox rows under test are the ones the
    /// controller would have written.
    /// </summary>
    private async Task<bool> MoveWithEventsAsync(
        Project project,
        Guid changeId,
        ProjectStatus from,
        ProjectStatus to)
    {
        var change = Change(project, changeId, from, to);

        return await _fixture.Repository.UpdateStatusAsync(
            change, SameSecond, ProjectEvents.ForStatusChange(project, change));
    }

    private static ProjectStatusChange Change(Project project, Guid changeId, ProjectStatus from, ProjectStatus to) => new()
    {
        Id = changeId,
        ProjectId = project.Id,
        FromStatus = from,
        ToStatus = to,
        ChangedByUserId = ProjectManagerId,
        ChangedByRole = PlatformRoles.ProjectManager,
        ChangedAt = SameSecond
    };
}
