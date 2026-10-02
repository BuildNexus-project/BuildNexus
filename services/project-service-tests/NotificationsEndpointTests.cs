using System.Security.Claims;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Controllers;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The US-26 AC-2 endpoints walked over a fake store: a person's stored notifications are what
/// they see on their next visit, and only theirs.
/// </summary>
/// <remarks>
/// Who may call which endpoint is decided by the role attribute, pinned in
/// <c>EndpointRoleDeclarationTests</c>; here the caller is simply the role the route lets
/// through. The SQL behind the store is <c>NotificationRepositoryDatabaseTests</c>.
/// </remarks>
public class NotificationsEndpointTests
{
    private static readonly Guid CallerId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid SomeoneElse = Guid.Parse("c3d4e5f6-1111-4222-8333-444455556666");
    private static readonly Guid ProjectId = Guid.Parse("e059b652-129d-4a69-a4ab-e5e95ed4b543");
    private static readonly DateTime Happened = new(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 30, 0, TimeSpan.Zero);

    // ----------------------------------- AC-2: visible on the next visit ----

    [Fact]
    public async Task A_stored_notification_is_visible_when_the_person_next_asks()
    {
        var stored = Stored(CallerId, "Milestone \"Foundation\" was completed on \"Villa\".");
        var (controller, _) = ControllerWith(stored);

        var list = await List(controller);

        var shown = Assert.Single(list.Notifications);
        Assert.Equal(stored.Id, shown.Id);
        Assert.Equal(ProjectId, shown.ProjectId);
        Assert.Equal("MilestoneCompleted", shown.EventType);
        Assert.Equal("Milestone \"Foundation\" was completed on \"Villa\".", shown.Message);
        Assert.Equal(Happened, shown.OccurredAt);
        Assert.False(shown.IsRead);
        Assert.Equal(1, list.UnreadCount);
    }

    [Fact]
    public async Task A_client_and_an_architect_each_see_their_own()
    {
        var mine = Stored(CallerId, "mine");
        var theirs = Stored(SomeoneElse, "theirs");
        var (controller, _) = ControllerWith(mine, theirs);

        var list = await List(controller);

        Assert.Equal([mine.Id], list.Notifications.Select(n => n.Id));
    }

    [Fact]
    public async Task The_list_is_for_the_caller_named_in_the_token()
    {
        var (controller, repository) = ControllerWith();

        await controller.ListNotifications(default);

        Assert.Equal(CallerId, repository.LastUserId);
    }

    [Fact]
    public async Task Newest_come_first()
    {
        var older = Stored(CallerId, "older", Happened);
        var newer = Stored(CallerId, "newer", Happened.AddHours(3));
        var (controller, _) = ControllerWith(older, newer);

        var list = await List(controller);

        Assert.Equal([newer.Id, older.Id], list.Notifications.Select(n => n.Id));
    }

    [Fact]
    public async Task Read_ones_are_listed_but_do_not_count_as_unread()
    {
        var (controller, _) = ControllerWith(
            Stored(CallerId, "seen", readAt: Happened.AddDays(1)),
            Stored(CallerId, "new"));

        var list = await List(controller);

        Assert.Equal(2, list.Notifications.Count);
        Assert.Equal(1, list.UnreadCount);
        Assert.Single(list.Notifications, n => n.IsRead);
    }

    [Fact]
    public async Task The_list_is_one_page_but_the_unread_count_is_the_whole_figure()
    {
        var many = Enumerable.Range(0, NotificationsController.DefaultTake + 5)
            .Select(hour => Stored(CallerId, $"n{hour}", Happened.AddHours(hour)))
            .ToArray();
        var (controller, _) = ControllerWith(many);

        var list = await List(controller);

        Assert.Equal(NotificationsController.DefaultTake, list.Notifications.Count);
        Assert.Equal(NotificationsController.DefaultTake + 5, list.UnreadCount);
        Assert.True(list.HasMore);
    }

    // ------------------------------------------------------------ paging ----

    [Fact]
    public async Task A_second_page_continues_exactly_where_the_first_stopped()
    {
        var all = Enumerable.Range(0, 7)
            .Select(hour => Stored(CallerId, $"n{hour}", Happened.AddHours(hour)))
            .ToArray();
        var (controller, _) = ControllerWith(all);

        var first = await List(controller, skip: 0, take: 3);
        var second = await List(controller, skip: 3, take: 3);
        var third = await List(controller, skip: 6, take: 3);

        var newestFirst = all.Reverse().Select(n => n.Id).ToList();
        Assert.Equal(newestFirst.Take(3), first.Notifications.Select(n => n.Id));
        Assert.Equal(newestFirst.Skip(3).Take(3), second.Notifications.Select(n => n.Id));
        Assert.Equal(newestFirst.Skip(6), third.Notifications.Select(n => n.Id));
    }

    [Fact]
    public async Task HasMore_is_true_until_the_last_page_and_false_on_it()
    {
        var (controller, _) = ControllerWith(
            Enumerable.Range(0, 7).Select(hour => Stored(CallerId, $"n{hour}", Happened.AddHours(hour))).ToArray());

        Assert.True((await List(controller, skip: 0, take: 3)).HasMore);
        Assert.True((await List(controller, skip: 3, take: 3)).HasMore);
        Assert.False((await List(controller, skip: 6, take: 3)).HasMore);
    }

    [Fact]
    public async Task A_page_that_ends_exactly_at_the_last_notification_has_no_more()
    {
        // Six notifications, pages of three: the second page is full, and there is nothing after it.
        var (controller, _) = ControllerWith(
            Enumerable.Range(0, 6).Select(hour => Stored(CallerId, $"n{hour}", Happened.AddHours(hour))).ToArray());

        var second = await List(controller, skip: 3, take: 3);

        Assert.Equal(3, second.Notifications.Count);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task Asking_past_the_end_is_an_empty_page_not_an_error()
    {
        var (controller, _) = ControllerWith(Stored(CallerId, "only"));

        var list = await List(controller, skip: 10, take: 5);

        Assert.Empty(list.Notifications);
        Assert.False(list.HasMore);
        Assert.Equal(1, list.UnreadCount);
    }

    [Fact]
    public async Task The_page_asked_for_reaches_the_query()
    {
        var (controller, repository) = ControllerWith();

        await controller.ListNotifications(default, skip: 40, take: 10);

        // One more than asked for, to learn whether there is a next page.
        Assert.Equal(11, repository.LastLimit);
        Assert.Equal(40, repository.LastOffset);
    }

    [Fact]
    public async Task Without_a_page_the_first_twenty_are_returned()
    {
        var (controller, repository) = ControllerWith();

        await controller.ListNotifications(default);

        Assert.Equal(NotificationsController.DefaultTake + 1, repository.LastLimit);
        Assert.Equal(0, repository.LastOffset);
    }

    [Fact]
    public async Task The_largest_page_allowed_is_accepted()
    {
        var (controller, _) = ControllerWith();

        Assert.IsType<OkObjectResult>(
            await controller.ListNotifications(default, take: NotificationsController.MaxTake));
    }

    [Theory]
    [InlineData(0, 0, "take")]
    [InlineData(0, -1, "take")]
    [InlineData(0, 51, "take")]
    [InlineData(-1, 20, "skip")]
    public async Task A_page_out_of_range_is_a_400_naming_it_and_reads_nothing(int skip, int take, string field)
    {
        // Never quietly clamped: a caller that asked for 500 and was handed 50 would believe it
        // had reached the end of the list.
        var (controller, repository) = ControllerWith(Stored(CallerId, "x"));

        var result = await controller.ListNotifications(default, skip, take);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        Assert.Contains(field, problem.Errors.Keys);
        Assert.Null(repository.LastLimit);
    }

    [Fact]
    public async Task A_person_with_nothing_stored_gets_an_empty_list_not_an_error()
    {
        var (controller, _) = ControllerWith();

        var list = await List(controller);

        Assert.Empty(list.Notifications);
        Assert.Equal(0, list.UnreadCount);
    }

    // ------------------------------------------------------- marking read ----

    [Fact]
    public async Task Marking_one_read_stamps_it_with_the_time_now_and_answers_204()
    {
        var stored = Stored(CallerId, "x");
        var (controller, repository) = ControllerWith(stored);

        var result = await controller.MarkNotificationRead(stored.Id, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(Now.UtcDateTime, repository.LastReadAtUtc);
        Assert.Equal(0, (await List(controller)).UnreadCount);
    }

    [Fact]
    public async Task Marking_one_read_twice_is_still_204()
    {
        var stored = Stored(CallerId, "x");
        var (controller, _) = ControllerWith(stored);

        await controller.MarkNotificationRead(stored.Id, default);
        var again = await controller.MarkNotificationRead(stored.Id, default);

        Assert.IsType<NoContentResult>(again);
    }

    [Fact]
    public async Task Someone_elses_notification_is_a_404_and_stays_unread()
    {
        // The same answer as one that does not exist, so a guessed id confirms nothing.
        var theirs = Stored(SomeoneElse, "theirs");
        var (controller, repository) = ControllerWith(theirs);

        var result = await controller.MarkNotificationRead(theirs.Id, default);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(Assert.Single(repository.Stored).IsRead);
    }

    [Fact]
    public async Task A_notification_that_does_not_exist_is_a_404()
    {
        var (controller, _) = ControllerWith();

        Assert.IsType<NotFoundResult>(await controller.MarkNotificationRead(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Marking_all_read_clears_the_callers_unread_and_leaves_everyone_elses()
    {
        var (controller, repository) = ControllerWith(
            Stored(CallerId, "a"),
            Stored(CallerId, "b"),
            Stored(SomeoneElse, "theirs"));

        var result = await controller.MarkAllNotificationsRead(default);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(Now.UtcDateTime, repository.LastReadAtUtc);
        Assert.Equal(0, (await List(controller)).UnreadCount);
        Assert.Equal(1, await repository.CountUnreadAsync(SomeoneElse));
    }

    [Fact]
    public async Task Marking_all_read_with_nothing_unread_is_still_204()
    {
        var (controller, _) = ControllerWith();

        Assert.IsType<NoContentResult>(await controller.MarkAllNotificationsRead(default));
    }

    // ------------------------------------------------- an unusable token ----

    [Fact]
    public async Task A_token_with_no_usable_subject_is_refused_on_every_endpoint()
    {
        var (controller, repository) = ControllerWith(subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.ListNotifications(default));
        Assert.IsType<UnauthorizedResult>(await controller.MarkNotificationRead(Guid.NewGuid(), default));
        Assert.IsType<UnauthorizedResult>(await controller.MarkAllNotificationsRead(default));
        Assert.Null(repository.LastUserId);
    }

    // ----------------------------------------------------------- helpers ----

    private static async Task<NotificationListResponse> List(
        NotificationsController controller,
        int skip = 0,
        int take = NotificationsController.DefaultTake) =>
        Assert.IsType<NotificationListResponse>(
            Assert.IsType<OkObjectResult>(await controller.ListNotifications(default, skip, take)).Value);

    private static Notification Stored(
        Guid userId,
        string message,
        DateTime? occurredAt = null,
        DateTime? readAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = ProjectId,
            EventId = Guid.NewGuid(),
            EventType = NotificationEventTypes.MilestoneCompleted,
            Message = message,
            OccurredAt = occurredAt ?? Happened,
            ReadAt = readAt
        };

    private static (NotificationsController Controller, FakeNotificationRepository Repository) ControllerWith(
        params Notification[] notifications) =>
        ControllerWith(subject: null, notifications);

    private static (NotificationsController Controller, FakeNotificationRepository Repository) ControllerWith(
        string? subject,
        params Notification[] notifications)
    {
        var repository = new FakeNotificationRepository();
        repository.Seed(notifications);

        var controller = new NotificationsController(repository, new FixedClock(Now))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The claims a token validated by this service leaves behind: "sub" and
                    // "role" in their short form, not rewritten into the WS-Federation URIs.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, subject ?? CallerId.ToString()),
                        new Claim(JwtOptions.RoleClaimType, "Client")
                    ], "TestAuth"))
                }
            }
        };

        return (controller, repository);
    }

    /// <summary>A clock that always says the same moment.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
