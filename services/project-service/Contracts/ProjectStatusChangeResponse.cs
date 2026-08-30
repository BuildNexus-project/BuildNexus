using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// One entry of a project's status history, as returned to callers.
/// </summary>
public class ProjectStatusChangeResponse
{
    public Guid Id { get; set; }

    /// <summary>
    /// The status left behind, or <c>null</c> on the opening entry — the
    /// project did not come from anywhere, it was created.
    /// </summary>
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    /// <summary>
    /// Who made the change, as the User Service knows them.
    /// </summary>
    /// <remarks>
    /// An id and not a name. The account lives in the User Service's own
    /// database and this service holds no copy of it — a name stored here would
    /// go stale the moment they changed it, and querying across the boundary is
    /// not something a service does.
    /// </remarks>
    public Guid ChangedByUserId { get; set; }

    /// <summary>
    /// The role they held at the time, so the entry reads as "the Project
    /// Manager moved it" rather than as a bare id.
    /// </summary>
    public string ChangedByRole { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    public static ProjectStatusChangeResponse From(ProjectStatusChange change) => new()
    {
        Id = change.Id,
        // Statuses go out as their names rather than the enum's numbers, so the
        // frontend reads what the database stores.
        FromStatus = change.FromStatus?.ToString(),
        ToStatus = change.ToStatus.ToString(),
        ChangedByUserId = change.ChangedByUserId,
        ChangedByRole = change.ChangedByRole,
        ChangedAt = change.ChangedAt
    };
}
