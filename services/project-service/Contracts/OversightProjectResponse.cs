using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// One project on the Admin's oversight screen (US-38): where it stands, who is
/// on it, when it last moved, and whether it has stalled.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="ProjectDetailResponse"/> — no
/// requirements and no status history. The screen is a list of every project on
/// the platform, and a row only has to say enough to decide which one to open.
/// </remarks>
public class OversightProjectResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>The status name, as the database stores it.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The Architect on the project, or <c>null</c> while nobody is.</summary>
    public Guid? AssignedArchitectId { get; set; }

    /// <summary>
    /// The Architect's name, or <c>null</c> while nobody is assigned <em>or</em>
    /// when the name could not be found out — the id is still there to fall back on.
    /// </summary>
    public string? AssignedArchitectName { get; set; }

    /// <summary>The Project Manager on the project, or <c>null</c> while nobody is.</summary>
    public Guid? AssignedProjectManagerId { get; set; }

    /// <summary>The Project Manager's name, on the same terms as <see cref="AssignedArchitectName"/>.</summary>
    public string? AssignedProjectManagerName { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the project last moved — the date the screen shows as "last updated".</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Whether <see cref="ProjectStallPolicy"/> calls the project stalled. Decided
    /// here rather than on the screen so the rule has one home.
    /// </summary>
    public bool IsStalled { get; set; }

    public static OversightProjectResponse From(
        Project project,
        IReadOnlyDictionary<Guid, string> names,
        DateTime nowUtc) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Location = project.Location,
        Status = project.Status.ToString(),
        AssignedArchitectId = project.AssignedArchitectId,
        AssignedArchitectName = NameOf(project.AssignedArchitectId, names),
        AssignedProjectManagerId = project.AssignedProjectManagerId,
        AssignedProjectManagerName = NameOf(project.AssignedProjectManagerId, names),
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt,
        IsStalled = ProjectStallPolicy.IsStalled(project.Status, project.UpdatedAt, nowUtc)
    };

    private static string? NameOf(Guid? userId, IReadOnlyDictionary<Guid, string> names) =>
        userId is { } id && names.TryGetValue(id, out var name) ? name : null;
}
