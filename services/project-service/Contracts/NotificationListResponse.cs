namespace BuildNexus.ProjectService.Contracts;

/// <summary>The person's recent notifications and how many they have not yet read (US-26 AC-2).</summary>
public class NotificationListResponse
{
    /// <summary>
    /// Every unread notification the person has — not only the ones in <see cref="Notifications"/>.
    /// The list is capped; the count is not, so "12 new" is true even when 10 are shown.
    /// </summary>
    public int UnreadCount { get; set; }

    /// <summary>Newest first, read and unread alike.</summary>
    public List<NotificationResponse> Notifications { get; set; } = [];
}
