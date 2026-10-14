using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>One notification, as the person reads it (US-26).</summary>
public class NotificationResponse
{
    public Guid Id { get; set; }

    /// <summary>So the screen can link to the project the notification is about.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>The kind: <c>DesignApproved</c>, <c>MilestoneCompleted</c> or <c>PaymentReceived</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The finished sentence.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>When the event happened, in UTC.</summary>
    public DateTime OccurredAt { get; set; }

    public bool IsRead { get; set; }

    public static NotificationResponse From(Notification notification) => new()
    {
        Id = notification.Id,
        ProjectId = notification.ProjectId,
        EventType = notification.EventType,
        Message = notification.Message,
        OccurredAt = notification.OccurredAt,
        IsRead = notification.IsRead
    };
}
