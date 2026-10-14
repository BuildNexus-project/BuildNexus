namespace BuildNexus.ProjectService.Models;

/// <summary>How many projects are in one status — one line of the Admin dashboard's tally.</summary>
public class ProjectStatusCount
{
    public ProjectStatus Status { get; init; }

    public int Count { get; init; }
}
