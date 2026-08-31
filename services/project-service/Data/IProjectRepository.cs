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
    /// history, in one transaction.
    /// </summary>
    /// <remarks>
    /// The two are written together on purpose: a project whose history does
    /// not start at its creation has an audit trail that begins halfway
    /// through, and there is no moment at which that is an acceptable state for
    /// the database to be in.
    /// </remarks>
    Task InsertAsync(Project project, ProjectStatusChange creation);

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
    /// Moves a project to <see cref="ProjectStatusChange.ToStatus"/> and records
    /// the change, in one transaction.
    /// </summary>
    /// <remarks>
    /// The update is conditional on the project still holding
    /// <see cref="ProjectStatusChange.FromStatus"/>. Two people moving the same
    /// project at once would otherwise both read <c>Designing</c>, both decide
    /// their move is valid, and both write — leaving one transition in the
    /// status and two in the history.
    /// </remarks>
    /// <returns>
    /// <c>false</c> if the project no longer holds the status it was read at,
    /// in which case nothing was written and the caller should re-read it.
    /// </returns>
    Task<bool> UpdateStatusAsync(ProjectStatusChange change, DateTime updatedAtUtc);
}
