using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// Data access for the <c>projects</c> table and the
/// <c>project_status_history</c> rows that belong to it.
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Inserts a new project row together with the opening entry of its status
    /// history and any events it raises, in one transaction.
    /// </summary>
    /// <remarks>
    /// All three are written together on purpose. A project whose history does
    /// not start at its creation has an audit trail that begins halfway
    /// through, and there is no moment at which that is an acceptable state for
    /// the database to be in.
    /// <para>
    /// <paramref name="outboxEvents"/> rides along for the same reason, and it
    /// is the whole of what makes a publish reliable: the event commits with
    /// the row it announces or not at all, so there is no window in which a
    /// project exists and nothing was ever going to tell anyone. Getting it
    /// onto Kafka afterwards is the dispatcher's job.
    /// </para>
    /// </remarks>
    Task InsertAsync(
        Project project,
        ProjectStatusChange creation,
        IReadOnlyList<OutboxEvent> outboxEvents);

    /// <summary>The project, or <c>null</c> if there is no such id.</summary>
    Task<Project?> GetByIdAsync(Guid id);

    /// <summary>Every project, newest first. For callers allowed to see all of them.</summary>
    Task<IReadOnlyList<Project>> ListAllAsync();

    /// <summary>
    /// The projects one user is involved in — the ones they submitted, plus the
    /// ones they are the assigned Architect or Project Manager on — newest
    /// first.
    /// </summary>
    Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId);

    /// <summary>
    /// A project's status history, oldest first, so it reads as the order things
    /// happened in.
    /// </summary>
    Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId);

    /// <summary>
    /// Moves a project to <see cref="ProjectStatusChange.ToStatus"/>, records
    /// the change, and enqueues the events it raises — in one transaction.
    /// </summary>
    /// <remarks>
    /// The update is conditional on the project still holding
    /// <see cref="ProjectStatusChange.FromStatus"/>. Two people moving the same
    /// project at once would otherwise both read <c>Designing</c>, both decide
    /// their move is valid, and both write — leaving one transition in the
    /// status and two in the history.
    /// <para>
    /// The guard is what makes enqueueing here safe as well as convenient: the
    /// loser of that race writes nothing, so it announces nothing either. An
    /// event published outside the transaction could describe a move that never
    /// happened.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>false</c> if the project no longer holds the status it was read at,
    /// in which case nothing was written — no status, no history, no events —
    /// and the caller should re-read it.
    /// </returns>
    Task<bool> UpdateStatusAsync(
        ProjectStatusChange change,
        DateTime updatedAtUtc,
        IReadOnlyList<OutboxEvent> outboxEvents);
}
