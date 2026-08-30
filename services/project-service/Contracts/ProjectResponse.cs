namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// A project as returned to callers, exactly as it was stored.
/// </summary>
public class ProjectResponse
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
    /// The status name — <c>Pending</c> for a project that has just been
    /// submitted. Sent as its name rather than the enum's number so the
    /// frontend reads what the database stores.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
