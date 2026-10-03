using System.Text.Json;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// Turns an event off a topic into the notifications it should produce (US-26): which events
/// count, who they concern, and what they are told.
/// </summary>
/// <remarks>
/// Pure — no database, no broker, no clock — so every rule here can be read and tested as
/// "given this event and this project". The consumer around it does the plumbing.
/// <para>
/// Two steps, because the information arrives in two places. <see cref="Read"/> takes the message
/// apart; it finds a project id and nothing about people, since events never say who. The caller
/// then loads that project, and <see cref="ToNotifications"/> decides who it concerns.
/// </para>
/// <para>
/// <b>Who is told.</b> The project's Client and its assigned Architect, for all three events —
/// the two roles US-26 names. The Project Manager and Admin are not recipients. The person who
/// caused the event is told too: a Client who pays gets a receipt, which is the useful reading
/// of "a payment is received", and leaving the actor out would leave the Client with nothing for
/// two of the three events.
/// </para>
/// <para>
/// <b>What they are told.</b> Deliberately no payment amount. The Architect is a recipient and
/// the Payment Service refuses them every one of its endpoints, so a figure here would show them
/// what they are otherwise not allowed to see.
/// </para>
/// </remarks>
public static class NotificationMapper
{
    /// <summary>
    /// The width of <c>notifications.message</c>. A name longer than its own column allows is not
    /// expected, but a message the database refuses would be retried forever and hold the
    /// partition, so the sentence is cut to fit rather than trusted to.
    /// </summary>
    public const int MaxMessageLength = 500;

    /// <summary>Whether this service turns an event of this type into notifications.</summary>
    public static bool Handles(string eventType) => NotificationEventTypes.All.Contains(eventType);

    /// <summary>
    /// Takes one message apart. Answers <c>null</c> for an event type that is not one of the
    /// three — it is another event on the same topic, and none of this service's business.
    /// </summary>
    /// <exception cref="JsonException">
    /// The event is one we handle but cannot be read: no payload, no project, no usable id, or a
    /// name missing. It will not read any better on a retry, so the consumer commits past it.
    /// </exception>
    public static NotifiableEvent? Read(IncomingEvent envelope)
    {
        if (!Handles(envelope.EventType))
        {
            return null;
        }

        // An empty id would collide with every other empty id on the (event, user) key, and the
        // second event would be mistaken for a redelivery of the first and silently dropped.
        if (envelope.EventId == Guid.Empty)
        {
            throw new JsonException($"A {envelope.EventType} event carried no eventId.");
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"A {envelope.EventType} event carried no payload object.");
        }

        var (projectId, whatHappened) = envelope.EventType switch
        {
            NotificationEventTypes.DesignApproved => ReadDesignApproved(envelope.Payload),
            NotificationEventTypes.MilestoneCompleted => ReadMilestoneCompleted(envelope.Payload),
            _ => ReadPaymentReceived(envelope.Payload)
        };

        if (projectId == Guid.Empty)
        {
            throw new JsonException($"The {envelope.EventType} payload was missing its projectId.");
        }

        return new NotifiableEvent(
            envelope.EventType,
            envelope.EventId,
            envelope.OccurredAt.UtcDateTime,
            projectId,
            whatHappened);
    }

    /// <summary>
    /// The notifications this event produces for this project: one for its Client, and one for its
    /// assigned Architect if it has one.
    /// </summary>
    /// <remarks>
    /// Each carries a fresh id of its own but the event's id, so the same event arriving again
    /// builds different rows that the database still recognises as the same notification.
    /// </remarks>
    public static IReadOnlyList<Notification> ToNotifications(NotifiableEvent notifiable, Project project)
    {
        var message = Fit($"{notifiable.WhatHappened} on \"{project.Name}\".");

        return Recipients(project)
            .Select(person => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = person,
                ProjectId = project.Id,
                EventId = notifiable.EventId,
                EventType = notifiable.EventType,
                Message = message,
                OccurredAt = notifiable.OccurredAtUtc
            })
            .ToList();
    }

    private static IEnumerable<Guid> Recipients(Project project)
    {
        yield return project.ClientId;

        // No Architect until an Admin assigns one. The same person in both seats is not expected,
        // but would be told once, not twice.
        if (project.AssignedArchitectId is { } architect && architect != project.ClientId)
        {
            yield return architect;
        }
    }

    private static (Guid ProjectId, string WhatHappened) ReadDesignApproved(JsonElement payload)
    {
        var approved = Deserialize<DesignApprovedPayload>(payload, "DesignApproved");
        var name = RequireName(approved.DocumentName, "DesignApproved", "documentName");

        var version = approved.VersionNumber > 0 ? $" (version {approved.VersionNumber})" : string.Empty;

        return (approved.ProjectId, $"Design \"{name}\"{version} was approved");
    }

    private static (Guid ProjectId, string WhatHappened) ReadMilestoneCompleted(JsonElement payload)
    {
        var completed = Deserialize<MilestoneCompletedPayload>(payload, "MilestoneCompleted");
        var name = RequireName(completed.Name, "MilestoneCompleted", "name");

        return (completed.ProjectId, $"Milestone \"{name}\" was completed");
    }

    private static (Guid ProjectId, string WhatHappened) ReadPaymentReceived(JsonElement payload)
    {
        var received = Deserialize<PaymentReceivedPayload>(payload, "PaymentReceived");

        return (received.ProjectId, "A payment was received");
    }

    private static T Deserialize<T>(JsonElement payload, string eventType) =>
        payload.Deserialize<T>(IncomingEvent.SerializerOptions)
            ?? throw new JsonException($"The {eventType} payload deserialised to null.");

    private static string RequireName(string name, string eventType, string field) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new JsonException($"The {eventType} payload was missing its {field}.")
            : name.Trim();

    private static string Fit(string message) =>
        message.Length <= MaxMessageLength ? message : string.Concat(message.AsSpan(0, MaxMessageLength - 1), "…");
}
