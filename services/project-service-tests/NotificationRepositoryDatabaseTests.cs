using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Models;
using MySqlConnector;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="Data.NotificationRepository"/> against real MySQL — what is stored, who can see
/// it, and the uniqueness rule that stops a redelivered event telling anyone twice (US-26).
/// </summary>
/// <remarks>
/// A stub cannot answer any of this. The deduplication is a unique key plus an
/// <c>ON DUPLICATE KEY UPDATE</c>; the foreign key and the CHECK constraint are the database
/// refusing bad rows; and "marking one read twice still succeeds" depends on whether the driver
/// reports rows matched or rows changed. Each person here is a fresh random id, so the tests
/// share a database without ever reading each other's rows.
/// <para>Needs <c>project-db</c> running — see <see cref="ProjectDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ProjectDatabaseCollection.Name)]
public class NotificationRepositoryDatabaseTests
{
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly DateTime Happened = new(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc);

    private readonly ProjectDatabaseFixture _fixture;

    public NotificationRepositoryDatabaseTests(ProjectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_stored_notification_reads_back_as_it_was_written()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var stored = NotificationFor(project, person, NotificationEventTypes.MilestoneCompleted, Happened);

        await _fixture.Notifications.InsertAsync([stored]);

        var read = Assert.Single(await _fixture.Notifications.ListForUserAsync(person, 10));
        Assert.Equal(stored.Id, read.Id);
        Assert.Equal(person, read.UserId);
        Assert.Equal(project.Id, read.ProjectId);
        Assert.Equal(stored.EventId, read.EventId);
        Assert.Equal(NotificationEventTypes.MilestoneCompleted, read.EventType);
        Assert.Equal(stored.Message, read.Message);
        Assert.Equal(Happened, read.OccurredAt);
        Assert.Null(read.ReadAt);
        Assert.False(read.IsRead);
    }

    [Fact]
    public async Task Times_come_back_labelled_UTC_so_they_serialise_with_a_Z()
    {
        // MySQL hands a DATETIME back as Unspecified. Left that way it would be sent without a
        // zone, and a browser would read it as the viewer's own local time.
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();

        await _fixture.Notifications.InsertAsync([NotificationFor(project, person, occurredAt: Happened)]);
        await _fixture.Notifications.MarkAllReadAsync(person, Happened.AddHours(1));

        var read = Assert.Single(await _fixture.Notifications.ListForUserAsync(person, 10));
        Assert.Equal(DateTimeKind.Utc, read.OccurredAt.Kind);
        Assert.Equal(DateTimeKind.Utc, read.ReadAt!.Value.Kind);
    }

    [Fact]
    public async Task A_person_sees_their_own_notifications_newest_first_and_nobody_elses()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var older = NotificationFor(project, person, occurredAt: Happened);
        var newer = NotificationFor(project, person, occurredAt: Happened.AddHours(2));
        var middle = NotificationFor(project, person, occurredAt: Happened.AddHours(1));

        await _fixture.Notifications.InsertAsync([older, newer, middle]);
        await _fixture.Notifications.InsertAsync([NotificationFor(project, someoneElse, occurredAt: Happened)]);

        var listed = await _fixture.Notifications.ListForUserAsync(person, 10);

        Assert.Equal([newer.Id, middle.Id, older.Id], listed.Select(n => n.Id));
    }

    [Fact]
    public async Task The_list_stops_at_the_limit_and_keeps_the_newest()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var all = Enumerable.Range(0, 5)
            .Select(hour => NotificationFor(project, person, occurredAt: Happened.AddHours(hour)))
            .ToList();

        await _fixture.Notifications.InsertAsync(all);

        var listed = await _fixture.Notifications.ListForUserAsync(person, 3);

        Assert.Equal(all.AsEnumerable().Reverse().Take(3).Select(n => n.Id), listed.Select(n => n.Id));
    }

    [Fact]
    public async Task A_person_with_no_notifications_gets_an_empty_list_and_a_zero_count()
    {
        var person = Guid.NewGuid();

        Assert.Empty(await _fixture.Notifications.ListForUserAsync(person, 10));
        Assert.Equal(0, await _fixture.Notifications.CountUnreadAsync(person));
    }

    // ------------------------------------------------------ redelivery ----

    [Fact]
    public async Task The_same_event_delivered_twice_tells_the_same_person_once()
    {
        // Kafka delivers at least once, so a restart or a rebalance replays events. The second
        // pass builds fresh notification ids for the same event — it is the (event, user) key,
        // not the row id, that has to catch it.
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await _fixture.Notifications.InsertAsync([NotificationFor(project, person, eventId: eventId)]);
        await _fixture.Notifications.InsertAsync([NotificationFor(project, person, eventId: eventId)]);

        Assert.Single(await _fixture.Notifications.ListForUserAsync(person, 10));
    }

    [Fact]
    public async Task A_redelivery_does_not_undo_a_notification_the_person_has_already_read()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await _fixture.Notifications.InsertAsync([NotificationFor(project, person, eventId: eventId)]);
        await _fixture.Notifications.MarkAllReadAsync(person, Happened);
        await _fixture.Notifications.InsertAsync([NotificationFor(project, person, eventId: eventId)]);

        Assert.Equal(0, await _fixture.Notifications.CountUnreadAsync(person));
    }

    [Fact]
    public async Task One_event_can_tell_two_different_people()
    {
        var project = await CreateProjectAsync();
        var client = Guid.NewGuid();
        var architect = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await _fixture.Notifications.InsertAsync(
        [
            NotificationFor(project, client, eventId: eventId),
            NotificationFor(project, architect, eventId: eventId)
        ]);

        Assert.Single(await _fixture.Notifications.ListForUserAsync(client, 10));
        Assert.Single(await _fixture.Notifications.ListForUserAsync(architect, 10));
    }

    [Fact]
    public async Task A_batch_is_stored_all_or_not_at_all()
    {
        // If the second of two rows is refused, the first must not be left behind: the consumer
        // retries the whole event, and a half-stored one would then be skipped as "already told".
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var good = NotificationFor(project, person);
        var bad = NotificationFor(project, person, eventType: "NotARealEventType");

        await Assert.ThrowsAsync<MySqlException>(() => _fixture.Notifications.InsertAsync([good, bad]));

        Assert.Empty(await _fixture.Notifications.ListForUserAsync(person, 10));
    }

    [Fact]
    public async Task Storing_nothing_is_a_no_op()
    {
        await _fixture.Notifications.InsertAsync([]);
    }

    // ------------------------------------------- what the database refuses ----

    [Fact]
    public async Task A_notification_for_a_project_that_does_not_exist_is_refused_not_swallowed()
    {
        // ON DUPLICATE KEY UPDATE, not INSERT IGNORE: IGNORE would turn this foreign-key failure
        // into a warning and the notification would silently never exist.
        var missing = new Project { Id = Guid.NewGuid() };

        await Assert.ThrowsAsync<MySqlException>(
            () => _fixture.Notifications.InsertAsync([NotificationFor(missing, Guid.NewGuid())]));
    }

    [Fact]
    public async Task An_event_type_the_story_does_not_name_is_refused()
    {
        var project = await CreateProjectAsync();

        await Assert.ThrowsAsync<MySqlException>(() => _fixture.Notifications.InsertAsync(
            [NotificationFor(project, Guid.NewGuid(), eventType: "ProjectCancelled")]));
    }

    [Theory]
    [InlineData(NotificationEventTypes.DesignApproved)]
    [InlineData(NotificationEventTypes.MilestoneCompleted)]
    [InlineData(NotificationEventTypes.PaymentReceived)]
    public async Task Every_type_in_the_list_is_accepted_by_the_database(string eventType)
    {
        // The CHECK constraint and NotificationEventTypes are two copies of one list. This is
        // what keeps them from drifting: add a type to the code without the migration and the
        // first insert of it is refused here, not in production.
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();

        await _fixture.Notifications.InsertAsync([NotificationFor(project, person, eventType: eventType)]);

        Assert.Single(await _fixture.Notifications.ListForUserAsync(person, 10));
    }

    // ------------------------------------------------------ reading them ----

    [Fact]
    public async Task Only_unread_notifications_are_counted_and_the_count_is_not_capped_by_the_list()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var all = Enumerable.Range(0, 5)
            .Select(hour => NotificationFor(project, person, occurredAt: Happened.AddHours(hour)))
            .ToList();
        await _fixture.Notifications.InsertAsync(all);

        await _fixture.Notifications.MarkReadAsync(all[0].Id, person, Happened.AddDays(1));

        // Five exist, one is read, and a list of two would only show two of the four unread.
        Assert.Equal(4, await _fixture.Notifications.CountUnreadAsync(person));
        Assert.Equal(2, (await _fixture.Notifications.ListForUserAsync(person, 2)).Count);
    }

    [Fact]
    public async Task Marking_one_read_stamps_it_and_leaves_the_rest_unread()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var first = NotificationFor(project, person, occurredAt: Happened);
        var second = NotificationFor(project, person, occurredAt: Happened.AddHours(1));
        await _fixture.Notifications.InsertAsync([first, second]);
        var readAt = Happened.AddDays(1);

        var marked = await _fixture.Notifications.MarkReadAsync(first.Id, person, readAt);

        Assert.True(marked);
        var listed = (await _fixture.Notifications.ListForUserAsync(person, 10)).ToDictionary(n => n.Id);
        Assert.Equal(readAt, listed[first.Id].ReadAt);
        Assert.Null(listed[second.Id].ReadAt);
    }

    [Fact]
    public async Task Marking_one_read_a_second_time_still_succeeds_and_keeps_the_first_time()
    {
        // A double-click, or a stale screen, must not turn into a "not found" error — and it
        // must not move the time it was first read.
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var stored = NotificationFor(project, person);
        await _fixture.Notifications.InsertAsync([stored]);
        var firstRead = Happened.AddDays(1);

        await _fixture.Notifications.MarkReadAsync(stored.Id, person, firstRead);
        var again = await _fixture.Notifications.MarkReadAsync(stored.Id, person, firstRead.AddDays(5));

        Assert.True(again);
        var read = Assert.Single(await _fixture.Notifications.ListForUserAsync(person, 10));
        Assert.Equal(firstRead, read.ReadAt);
    }

    [Fact]
    public async Task Someone_elses_notification_cannot_be_marked_read_and_looks_like_it_does_not_exist()
    {
        var project = await CreateProjectAsync();
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var stored = NotificationFor(project, owner);
        await _fixture.Notifications.InsertAsync([stored]);

        var marked = await _fixture.Notifications.MarkReadAsync(stored.Id, intruder, Happened.AddDays(1));

        Assert.False(marked);
        Assert.Equal(1, await _fixture.Notifications.CountUnreadAsync(owner));
    }

    [Fact]
    public async Task A_notification_that_does_not_exist_cannot_be_marked_read()
    {
        Assert.False(await _fixture.Notifications.MarkReadAsync(Guid.NewGuid(), Guid.NewGuid(), Happened));
    }

    [Fact]
    public async Task Marking_all_read_clears_the_persons_unread_and_touches_nobody_elses()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        await _fixture.Notifications.InsertAsync(
        [
            NotificationFor(project, person),
            NotificationFor(project, person),
            NotificationFor(project, someoneElse)
        ]);

        var marked = await _fixture.Notifications.MarkAllReadAsync(person, Happened.AddDays(1));

        Assert.Equal(2, marked);
        Assert.Equal(0, await _fixture.Notifications.CountUnreadAsync(person));
        Assert.Equal(1, await _fixture.Notifications.CountUnreadAsync(someoneElse));
    }

    [Fact]
    public async Task Marking_all_read_counts_only_what_it_actually_changed()
    {
        var project = await CreateProjectAsync();
        var person = Guid.NewGuid();
        var alreadyRead = NotificationFor(project, person);
        await _fixture.Notifications.InsertAsync([alreadyRead, NotificationFor(project, person)]);
        var firstRead = Happened.AddDays(1);
        await _fixture.Notifications.MarkReadAsync(alreadyRead.Id, person, firstRead);

        var marked = await _fixture.Notifications.MarkAllReadAsync(person, firstRead.AddDays(3));

        Assert.Equal(1, marked);
        Assert.Equal(0, await _fixture.Notifications.MarkAllReadAsync(person, firstRead.AddDays(4)));

        // And the one that was already read kept the time it was first read.
        var kept = (await _fixture.Notifications.ListForUserAsync(person, 10)).Single(n => n.Id == alreadyRead.Id);
        Assert.Equal(firstRead, kept.ReadAt);
    }

    // ----------------------------------------------------------- helpers ----

    private static Notification NotificationFor(
        Project project,
        Guid person,
        string eventType = NotificationEventTypes.DesignApproved,
        DateTime? occurredAt = null,
        Guid? eventId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = person,
            ProjectId = project.Id,
            EventId = eventId ?? Guid.NewGuid(),
            EventType = eventType,
            Message = $"Something happened on \"{project.Name}\".",
            OccurredAt = occurredAt ?? Happened
        };

    /// <summary>A stored project in this run's namespace, so the fixture's cleanup finds it.</summary>
    private async Task<Project> CreateProjectAsync()
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
            CreatedAt = Happened.AddMonths(-1),
            UpdatedAt = Happened.AddMonths(-1)
        };

        await _fixture.Repository.InsertAsync(
            project, ProjectStatusChange.ForCreation(project, PlatformRoles.Client), []);

        return project;
    }
}
