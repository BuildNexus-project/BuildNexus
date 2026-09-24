using System.ComponentModel.DataAnnotations;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// What a Project Manager submits to add a milestone to an approved project
/// (US-12). The project id comes from the route, and the initial status is
/// always <c>NotStarted</c> — the caller has no say in that.
/// </summary>
public class CreateMilestoneRequest
{
    /// <summary>
    /// The PM's own label for this milestone. Trimmed by the controller; a
    /// blank or whitespace-only value is refused by <c>[Required]</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A milestone name is required.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Milestone name must not exceed 150 characters.")]
    public string Name { get; set; } = string.Empty;
}