namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// One project the Project Service says the caller may see — the facts this service needs
/// from it.
/// </summary>
/// <param name="Id">The project's id, which every row here carries as a plain column.</param>
/// <param name="Name">
/// The project's name, so a dashboard can say which build it means without the page asking
/// the Project Service a second time.
/// </param>
public sealed record VisibleProject(Guid Id, string Name);
