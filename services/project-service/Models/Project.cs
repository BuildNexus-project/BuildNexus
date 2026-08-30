namespace BuildNexus.ProjectService.Models;

/// <summary>
/// A construction project a Client has submitted, mapped by hand from the
/// <c>projects</c> table.
/// </summary>
public class Project
{
    public Guid Id { get; set; }

    /// <summary>
    /// The Client who submitted it, taken from the <c>sub</c> claim of their
    /// token.
    /// </summary>
    /// <remarks>
    /// Not a foreign key: the account lives in the User Service's own database,
    /// and this service holds no key across that boundary. The token has already
    /// proved the account exists and holds the Client role.
    /// </remarks>
    public Guid ClientId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>Land size in perches. Fractions are ordinary, so not an integer.</summary>
    public decimal LandSizePerches { get; set; }

    /// <summary>
    /// The Client's budget for the build. <c>decimal</c> rather than a
    /// floating-point type: money must not drift by a rounding error between
    /// what was typed and what was stored.
    /// </summary>
    public decimal Budget { get; set; }

    public int Floors { get; set; }

    public int Bedrooms { get; set; }

    public int Bathrooms { get; set; }

    /// <summary>
    /// How many vehicles the garage should hold. A count rather than a yes/no,
    /// so "no garage" and "two cars" are the same question — zero means none.
    /// </summary>
    public int GarageSpaces { get; set; }

    /// <summary>
    /// Anything else the Client asked for, in their own words. <c>null</c> when
    /// they had nothing to add — this is the one optional field on the form.
    /// </summary>
    public string? OtherRequirements { get; set; }

    /// <summary>Always <see cref="ProjectStatus.Pending"/> on a newly created project.</summary>
    public ProjectStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
