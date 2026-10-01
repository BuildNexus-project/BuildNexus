namespace BuildNexus.DesignService.Models;

/// <summary>
/// One project the Project Service says the caller may see — just the two
/// facts this service needs from it.
/// </summary>
/// <param name="Id">The project's id, which every design document here carries as a plain column.</param>
/// <param name="Status">
/// The project's lifecycle status, as the Project Service spells it
/// (<c>Pending</c>, <c>Designing</c>, ...). Kept as text: the lifecycle is that
/// service's to define, and this one only needs to tell finished work from
/// unfinished.
/// </param>
public sealed record VisibleProject(Guid Id, string Status);
