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

    /// <summary>
    /// Always <see cref="ProjectStatus.Pending"/> on a newly created project,
    /// and thereafter only what <see cref="ProjectStatusTransitions"/> allows.
    /// </summary>
    public ProjectStatus Status { get; set; }

    /// <summary>
    /// The Architect working on it, or <c>null</c> while nobody is.
    /// </summary>
    /// <remarks>
    /// Not a foreign key, for the same reason <see cref="ClientId"/> is not:
    /// the account lives in the User Service's own database. Only the id is
    /// held and never a copy of the person — a name kept here would go stale
    /// the moment they changed it.
    /// <para>
    /// Nothing writes this yet. No story has defined how staff are put on a
    /// project, so it is read-only for now: US-06 needs it because the view
    /// shows the assigned architect, and because it is half of what "assigned
    /// staff" means in the access rule.
    /// </para>
    /// </remarks>
    public Guid? AssignedArchitectId { get; set; }

    /// <summary>
    /// The Project Manager running it, or <c>null</c> while nobody is. The same
    /// remarks as <see cref="AssignedArchitectId"/> apply.
    /// </summary>
    public Guid? AssignedProjectManagerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
