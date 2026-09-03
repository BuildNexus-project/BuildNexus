using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// One row of the caller's project list — enough to recognise a project and
/// pick it, and no more.
/// </summary>
/// <remarks>
/// The requirements and the status history are deliberately absent: a listing
/// that carried them would send every project's full detail to draw a table of
/// names. They are read through <c>GET /api/projects/{id}</c>, one project at a
/// time.
/// </remarks>
public class ProjectSummaryResponse
{
    public Guid Id { get; set; }

    /// <summary>The Client the project belongs to.</summary>
    public Guid ClientId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>The status name, as the database stores it.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When it last moved. In a list ordered by creation, this is what says
    /// which projects are actually being worked on.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    public static ProjectSummaryResponse From(Project project) => new()
    {
        Id = project.Id,
        ClientId = project.ClientId,
        Name = project.Name,
        Location = project.Location,
        Status = project.Status.ToString(),
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt
    };
}
