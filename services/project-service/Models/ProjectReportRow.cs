namespace BuildNexus.ProjectService.Models;

/// <summary>
/// One project as the status report sees it — enough to recognise it and place
/// it in the pipeline, and no more.
/// </summary>
/// <remarks>
/// Narrower than <see cref="Project"/> on purpose: the report is a pipeline view,
/// so the requirements, the assigned staff and the payment fields stay out of
/// the query rather than being read and thrown away.
/// </remarks>
public class ProjectReportRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public ProjectStatus Status { get; set; }

    public decimal Budget { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
