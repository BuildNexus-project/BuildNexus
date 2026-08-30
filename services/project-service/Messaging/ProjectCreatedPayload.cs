using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The <c>payload</c> of a <see cref="ProjectEventTypes.ProjectCreated"/> event:
/// the whole project as it was stored.
/// </summary>
/// <remarks>
/// A full snapshot rather than just an id. A consumer in another service cannot
/// query this service's database to fill in the rest — one schema per service —
/// so an event carrying only an id would force a REST call back here for every
/// message, which is the coupling the message bus exists to avoid.
/// </remarks>
public sealed class ProjectCreatedPayload
{
    public required Guid ProjectId { get; init; }

    /// <summary>The Client who submitted it, as the User Service knows them.</summary>
    public required Guid ClientId { get; init; }

    public required string Name { get; init; }

    public required string Location { get; init; }

    public required decimal LandSizePerches { get; init; }

    public required decimal Budget { get; init; }

    public required int Floors { get; init; }

    public required int Bedrooms { get; init; }

    public required int Bathrooms { get; init; }

    public required int GarageSpaces { get; init; }

    /// <summary><c>null</c> when the Client had nothing to add.</summary>
    public required string? OtherRequirements { get; init; }

    /// <summary>
    /// <c>Pending</c> for a newly submitted project. Carried as the name rather
    /// than the enum's number, so a consumer is not tied to our ordering.
    /// </summary>
    public required string Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    public static ProjectCreatedPayload From(Project project) => new()
    {
        ProjectId = project.Id,
        ClientId = project.ClientId,
        Name = project.Name,
        Location = project.Location,
        LandSizePerches = project.LandSizePerches,
        Budget = project.Budget,
        Floors = project.Floors,
        Bedrooms = project.Bedrooms,
        Bathrooms = project.Bathrooms,
        GarageSpaces = project.GarageSpaces,
        OtherRequirements = project.OtherRequirements,
        Status = project.Status.ToString(),
        CreatedAt = project.CreatedAt
    };
}
