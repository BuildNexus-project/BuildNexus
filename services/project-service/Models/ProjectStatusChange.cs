namespace BuildNexus.ProjectService.Models;

/// <summary>
/// One entry in a project's status history, mapped by hand from the
/// <c>project_status_history</c> table: the project moved from one status to
/// another, and this is who did it and when.
/// </summary>
/// <remarks>
/// A record of something that already happened, so nothing ever updates one.
/// A correction is a further transition, not an edit to this row.
/// </remarks>
public class ProjectStatusChange
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>
    /// The status left behind, or <c>null</c> on the opening entry — a project
    /// did not come from anywhere, it was created.
    /// </summary>
    public ProjectStatus? FromStatus { get; set; }

    public ProjectStatus ToStatus { get; set; }

    /// <summary>Who caused it, taken from the <c>sub</c> claim of their token.</summary>
    public Guid ChangedByUserId { get; set; }

    /// <summary>
    /// The role they held at the time, as one of the four platform role names.
    /// Stored rather than looked up later: an audit trail that re-read today's
    /// role would rewrite history every time somebody is promoted.
    /// </summary>
    public string ChangedByRole { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Why the change was made, when there is a reason worth keeping — US-08's
    /// cancellation records the Client's or Admin's reason here. <c>null</c> for
    /// a move that speaks for itself, which is every transition so far.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// The opening entry for a project that has just been created — from
    /// nothing, to whatever status it was created in, by the Client who
    /// submitted it, at the moment it was stored.
    /// </summary>
    public static ProjectStatusChange ForCreation(Project project, string changedByRole) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = project.Id,
        FromStatus = null,
        ToStatus = project.Status,
        ChangedByUserId = project.ClientId,
        ChangedByRole = changedByRole,
        ChangedAt = project.CreatedAt
    };
}
