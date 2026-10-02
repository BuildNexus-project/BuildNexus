namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// An event that is worth telling people about (US-26), reduced to what the notification needs:
/// which project it is about and what happened.
/// </summary>
/// <remarks>
/// The step between a raw message and a stored notification. It exists because the two halves
/// need different things: reading the message yields a project id and nothing about people, and
/// only once the project has been loaded can the people be worked out.
/// </remarks>
/// <param name="EventType">One of <see cref="Models.NotificationEventTypes"/>.</param>
/// <param name="EventId">The envelope's <c>eventId</c> — what makes a redelivery recognisable.</param>
/// <param name="OccurredAtUtc">When it happened, off the envelope.</param>
/// <param name="ProjectId">The project it is about.</param>
/// <param name="WhatHappened">
/// The event-specific part of the sentence, without the project: <c>Milestone "Foundation" was
/// completed</c>.
/// </param>
public sealed record NotifiableEvent(
    string EventType,
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ProjectId,
    string WhatHappened);
