namespace BuildNexus.ProjectService.Models;

/// <summary>
/// The kinds of notification a person can be sent (US-26) — one per event the story names.
/// </summary>
/// <remarks>
/// Deliberately the same strings as the <c>eventType</c> of the event each one comes from, so a
/// stored notification can be traced back to the kind of event that caused it. Only the events
/// US-26 names belong here: <c>ck_notifications_event_type</c> holds the database to the same
/// list, and a type added without a migration would be refused at the first insert.
/// </remarks>
public static class NotificationEventTypes
{
    /// <summary>A Client has approved a design document version (published by the Design Service).</summary>
    public const string DesignApproved = nameof(DesignApproved);

    /// <summary>A milestone has been marked complete (published by the Construction Service).</summary>
    public const string MilestoneCompleted = nameof(MilestoneCompleted);

    /// <summary>A Client has recorded a payment against an invoice (published by the Payment Service).</summary>
    public const string PaymentReceived = nameof(PaymentReceived);

    /// <summary>
    /// Every type, for code that has to enumerate them — and for the test that holds the
    /// database's own CHECK constraint to the same set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [DesignApproved, MilestoneCompleted, PaymentReceived];
}
