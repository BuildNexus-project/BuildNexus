using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// One project inside a group of the status report (US-18) — enough to
/// recognise it and follow the link to it, and no more.
/// </summary>
/// <remarks>
/// A near-mirror of <see cref="ProjectReportRow"/>, kept separate so the wire
/// contract does not move if the model gains a field the report should not
/// expose. The status is sent as its name, as everywhere else in this service.
/// </remarks>
public class ProjectReportRowResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>The status name, as the database stores it.</summary>
    public string Status { get; set; } = string.Empty;

    public decimal Budget { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public static ProjectReportRowResponse From(ProjectReportRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Location = row.Location,
        Status = row.Status.ToString(),
        Budget = row.Budget,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };
}
