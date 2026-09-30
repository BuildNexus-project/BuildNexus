namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// The project half of a Client's dashboard (US-21 AC-1): their active projects.
/// </summary>
/// <remarks>
/// Only the half this service owns. Design status, build progress and payments
/// due live in the Design, Construction and Payment services' own databases and
/// arrive from their own dashboard endpoints; the page puts the four together.
/// </remarks>
public class ClientProjectsDashboardResponse
{
    /// <summary>How many active projects the Client has — the length of <see cref="Projects"/>.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Most recently moved first. Empty is a real answer for a Client with nothing under way.</summary>
    public IReadOnlyList<DashboardProjectResponse> Projects { get; set; } = [];
}
