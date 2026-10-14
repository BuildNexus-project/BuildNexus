using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>One project on a role dashboard — enough to recognise it and see where it stands.</summary>
public class DashboardProjectResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>The status name, as the database stores it.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>When it last moved.</summary>
    public DateTime UpdatedAt { get; set; }

    public static DashboardProjectResponse From(DashboardProject project) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Location = project.Location,
        Status = project.Status.ToString(),
        UpdatedAt = project.UpdatedAt
    };
}
