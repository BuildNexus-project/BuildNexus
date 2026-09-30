namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// The project half of an Architect's dashboard (US-21 AC-2): the active
/// projects they are assigned to.
/// </summary>
/// <remarks>
/// Pending design revisions are the Design Service's data and come from its own
/// dashboard endpoint; the page puts the two together.
/// </remarks>
public class ArchitectProjectsDashboardResponse
{
    /// <summary>How many active projects the Architect is assigned to — the length of <see cref="Projects"/>.</summary>
    public int AssignedCount { get; set; }

    /// <summary>Most recently moved first. Empty is a real answer for an Architect nobody has assigned yet.</summary>
    public IReadOnlyList<DashboardProjectResponse> Projects { get; set; } = [];
}
