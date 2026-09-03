using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The <c>payload</c> of a <see cref="ProjectEventTypes.ProjectUpdated"/> event:
/// which project moved, where it moved from and to, and who moved it.
/// </summary>
/// <remarks>
/// Deliberately not the whole project, unlike
/// <see cref="ProjectCreatedPayload"/>. US-22 asks for the project id and the
/// minimal context a consumer needs, and what a consumer needs here is the
/// <em>move</em> — the requirements have not changed, and repeating them on
/// every transition would put a project's entire description on the topic four
/// more times for nothing.
/// <para>
/// Both statuses are carried, not just the new one. A consumer that missed an
/// earlier message can tell from <see cref="PreviousStatus"/> whether its own
/// picture of the project is still current, which "it is now Construction"
/// alone does not say.
/// </para>
/// </remarks>
public sealed class ProjectUpdatedPayload
{
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// The Client whose project this is, as the User Service knows them.
    /// Carried because a consumer acting on the move — billing it, notifying
    /// somebody — needs to know whose project it is, and cannot query this
    /// service's database to find out.
    /// </summary>
    public required Guid ClientId { get; init; }

    /// <summary>
    /// So a consumer can name the project to a human without calling back here
    /// for it. The one field of the description repeated on a move.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The status it left, as its name. Never <c>null</c> in practice — only a
    /// project's creation comes from nowhere, and that is
    /// <see cref="ProjectEventTypes.ProjectCreated"/>, not this — but typed
    /// nullable because the history entry it is read from allows it.
    /// </summary>
    public required string? PreviousStatus { get; init; }

    /// <summary>
    /// Where it is now. Carried as the name rather than the enum's number, so a
    /// consumer is not tied to our ordering.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Who moved it, from the <c>sub</c> claim of their token, and the role they
    /// held while doing it — the same pair the status history records, so a
    /// consumer's own audit trail can agree with ours.
    /// </summary>
    public required Guid ChangedByUserId { get; init; }

    public required string ChangedByRole { get; init; }

    /// <summary>
    /// Offset-bearing, so the instant is unambiguous on the wire. A bare
    /// <see cref="DateTime"/> would serialise without a zone and leave a
    /// consumer to guess at one.
    /// </summary>
    public required DateTimeOffset ChangedAt { get; init; }

    public static ProjectUpdatedPayload From(Project project, ProjectStatusChange change) => new()
    {
        ProjectId = project.Id,
        ClientId = project.ClientId,
        Name = project.Name,
        PreviousStatus = change.FromStatus?.ToString(),
        Status = change.ToStatus.ToString(),
        ChangedByUserId = change.ChangedByUserId,
        ChangedByRole = change.ChangedByRole,
        ChangedAt = EventTimestamp.AsUtc(change.ChangedAt)
    };
}
