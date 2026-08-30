using System.ComponentModel.DataAnnotations;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// Payload for <c>POST /api/projects</c> — the requirements a Client submits for
/// a new construction project.
/// </summary>
/// <remarks>
/// The column limits and CHECK constraints in <c>projects</c> are mirrored here
/// so bad input is refused with a message naming the field, rather than reaching
/// MySQL and coming back as a constraint violation nobody can read.
/// <para>
/// The numeric fields are nullable on purpose. Left as plain <c>int</c> and
/// <c>decimal</c> a missing value would bind as zero, and "no bedrooms" would be
/// indistinguishable from "the caller forgot to say" — which matters, because
/// zero is a legitimate answer for bedrooms, bathrooms and garage spaces.
/// Nullable plus <c>[Required]</c> keeps the two apart.
/// </para>
/// <para>
/// The Client is not a field here. It is taken from the <c>sub</c> claim of the
/// caller's own token, so a project cannot be submitted on somebody else's
/// behalf by sending a different id.
/// </para>
/// </remarks>
public class CreateProjectRequest
{
    [Required(ErrorMessage = "Project name is required.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Project name must be between 3 and 150 characters.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the build is going — a town, an address, or a plot reference.</summary>
    [Required(ErrorMessage = "Location is required.")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Location must be between 2 and 255 characters.")]
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Land size in perches. The upper bound is what <c>DECIMAL(10,2)</c> holds,
    /// so a number too large for the column is refused here rather than
    /// overflowing it.
    /// </summary>
    [Required(ErrorMessage = "Land size is required.")]
    [Range(typeof(decimal), "0.01", "99999999.99",
        ConvertValueInInvariantCulture = true, ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Land size must be greater than 0 perches.")]
    public decimal? LandSizePerches { get; set; }

    /// <summary>
    /// The Client's budget for the build. The upper bound is what
    /// <c>DECIMAL(15,2)</c> holds.
    /// </summary>
    [Required(ErrorMessage = "Budget is required.")]
    [Range(typeof(decimal), "0.01", "9999999999999.99",
        ConvertValueInInvariantCulture = true, ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Budget must be greater than 0.")]
    public decimal? Budget { get; set; }

    /// <summary>At least one — a building with no floors is not a building.</summary>
    [Required(ErrorMessage = "Number of floors is required.")]
    [Range(1, 100, ErrorMessage = "Number of floors must be between 1 and 100.")]
    public int? Floors { get; set; }

    /// <summary>Zero is allowed: not every build is a house.</summary>
    [Required(ErrorMessage = "Number of bedrooms is required.")]
    [Range(0, 100, ErrorMessage = "Number of bedrooms must be between 0 and 100.")]
    public int? Bedrooms { get; set; }

    [Required(ErrorMessage = "Number of bathrooms is required.")]
    [Range(0, 100, ErrorMessage = "Number of bathrooms must be between 0 and 100.")]
    public int? Bathrooms { get; set; }

    /// <summary>
    /// How many vehicles the garage should hold. A count rather than a yes/no,
    /// so "no garage" and "two cars" are the same question — zero means none.
    /// </summary>
    [Required(ErrorMessage = "Number of garage spaces is required.")]
    [Range(0, 20, ErrorMessage = "Number of garage spaces must be between 0 and 20.")]
    public int? GarageSpaces { get; set; }

    /// <summary>
    /// Anything else the Client wants to say, in their own words. The only
    /// optional field on the form; blank is stored as <c>NULL</c>.
    /// </summary>
    /// <remarks>
    /// The column is <c>TEXT</c> and holds far more than this. The limit is the
    /// form's, not the column's: a requirements note is a paragraph or two, and
    /// an unbounded free-text field is an unbounded request body.
    /// </remarks>
    [StringLength(2000, ErrorMessage = "Other requirements must not exceed 2000 characters.")]
    public string? OtherRequirements { get; set; }
}
