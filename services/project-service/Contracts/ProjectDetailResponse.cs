using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// A project in full — its requirements, its status, who is on it, when it was
/// created, and every status change it has been through — which is the first
/// US-06 acceptance bullet in one response.
/// </summary>
/// <remarks>
/// One request rather than a project call plus a history call. The two are read
/// together every time the view is opened, and splitting them would let a
/// screen show a status and a history that disagree.
/// </remarks>
public class ProjectDetailResponse
{
    public Guid Id { get; set; }

    /// <summary>The Client the project belongs to.</summary>
    public Guid ClientId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public decimal LandSizePerches { get; set; }

    public decimal Budget { get; set; }

    public int Floors { get; set; }

    public int Bedrooms { get; set; }

    public int Bathrooms { get; set; }

    public int GarageSpaces { get; set; }

    /// <summary><c>null</c> when the Client had nothing to add.</summary>
    public string? OtherRequirements { get; set; }

    /// <summary>
    /// The status name. Sent as its name rather than the enum's number so the
    /// frontend reads what the database stores.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The Architect on the project, or <c>null</c> while nobody is.
    /// </summary>
    /// <remarks>
    /// An id and not a name, for the same reason as
    /// <see cref="ProjectStatusChangeResponse.ChangedByUserId"/>: the account
    /// lives in the User Service's own database.
    /// </remarks>
    public Guid? AssignedArchitectId { get; set; }

    /// <summary>The Project Manager on the project, or <c>null</c> while nobody is.</summary>
    public Guid? AssignedProjectManagerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Every status the project has been in, oldest first.</summary>
    public IReadOnlyList<ProjectStatusChangeResponse> StatusHistory { get; set; } = [];

    /// <summary>
    /// The statuses this project may move to next — one, or none once it is
    /// Completed.
    /// </summary>
    /// <remarks>
    /// Sent so the UI can offer exactly the moves that will be accepted rather
    /// than reimplementing the transition table and drifting from it. The
    /// service still checks the transition on the way in: this is a convenience
    /// for the caller, never the enforcement.
    /// <para>
    /// What the project <em>may</em> do, not what this caller may do — a Client
    /// reading their own project is shown the next step without being offered a
    /// button for it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> AllowedNextStatuses { get; set; } = [];

    public static ProjectDetailResponse From(Project project, IReadOnlyList<ProjectStatusChange> history) => new()
    {
        Id = project.Id,
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
        AssignedArchitectId = project.AssignedArchitectId,
        AssignedProjectManagerId = project.AssignedProjectManagerId,
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt,
        // Already ordered oldest first by the query that read them; nothing here
        // re-sorts, so the response cannot disagree with the table.
        StatusHistory = [.. history.Select(ProjectStatusChangeResponse.From)],
        AllowedNextStatuses = [.. ProjectStatusTransitions.NextFrom(project.Status).Select(status => status.ToString())]
    };
}
