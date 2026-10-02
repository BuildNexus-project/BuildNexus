using System.Security.Claims;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Controllers;

/// <summary>
/// A Client's or Architect's own notifications (US-26 AC-2): read them, and mark them read.
/// </summary>
/// <remarks>
/// Who is asking decides what comes back, and it is always the token's own <c>sub</c> — none of
/// these takes a user id. Nobody can read or dismiss another person's notification because there
/// is nowhere to say whose; and a notification id that is not theirs answers exactly like one that
/// does not exist.
/// <para>
/// Nothing is created here. Notifications are written by <c>NotificationEventsConsumer</c> from
/// events other stories already publish. These endpoints only show them, which is why a person's
/// next visit — a login, or any load of the dashboard — is when they appear: the page asks, and
/// whatever has been stored by then is the answer.
/// </para>
/// </remarks>
[ApiController]
[Route("api/projects/notifications")]
[Authorize(Roles = PlatformRoles.ClientOrArchitect)]
public class NotificationsController : ControllerBase
{
    /// <summary>
    /// How many notifications one read returns. Enough for what accumulates between visits; the
    /// unread count beside it is the whole figure, so a longer backlog is not hidden — only not
    /// all listed.
    /// </summary>
    public const int MaxListed = 20;

    private readonly INotificationRepository _notifications;
    private readonly TimeProvider _clock;

    public NotificationsController(INotificationRepository notifications, TimeProvider clock)
    {
        _notifications = notifications;
        _clock = clock;
    }

    /// <summary>
    /// The caller's most recent notifications and how many are unread (US-26 AC-2). Allowed roles:
    /// Client, Architect.
    /// </summary>
    /// <remarks>
    /// Newest first, read and unread alike, at most 20. A person nothing has happened for gets an
    /// empty list and a count of zero, which is the truthful answer rather than a refusal.
    /// </remarks>
    /// <response code="200">The caller's notifications and unread count.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client or an Architect.</response>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListNotifications(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        var notifications = await _notifications.ListForUserAsync(userId, MaxListed, cancellationToken);
        var unread = await _notifications.CountUnreadAsync(userId, cancellationToken);

        return Ok(new NotificationListResponse
        {
            UnreadCount = unread,
            Notifications = notifications.Select(NotificationResponse.From).ToList()
        });
    }

    /// <summary>
    /// Marks one of the caller's notifications read. Allowed roles: Client, Architect.
    /// </summary>
    /// <remarks>
    /// Repeating it is harmless and keeps the time it was first read.
    /// </remarks>
    /// <response code="204">It is marked read.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client or an Architect.</response>
    /// <response code="404">The caller has no such notification — it may belong to someone else.</response>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        var marked = await _notifications.MarkReadAsync(id, userId, _clock.GetUtcNow().UtcDateTime, cancellationToken);

        return marked ? NoContent() : NotFound();
    }

    /// <summary>
    /// Marks every one of the caller's unread notifications read. Allowed roles: Client, Architect.
    /// </summary>
    /// <remarks>
    /// Answers 204 even when there was nothing unread: the caller asked for "none unread", and
    /// that is now true.
    /// </remarks>
    /// <response code="204">Nothing of the caller's is unread any more.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Client or an Architect.</response>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        await _notifications.MarkAllReadAsync(userId, _clock.GetUtcNow().UtcDateTime, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// The caller's account id from the token's <c>sub</c> claim. Signature-valid but without a
    /// usable id is a token there is nobody to answer for.
    /// </summary>
    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
