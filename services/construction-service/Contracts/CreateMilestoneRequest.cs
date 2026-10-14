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

    /// <summary>
    /// The day the milestone should be finished by, as <c>yyyy-MM-dd</c>. Optional — leave it
    /// out, or send <c>null</c>, for a milestone with no date. A value that is not a real
    /// date is refused as a 400 before the action runs.
    /// </summary>
    public DateOnly? DueDate { get; set; }
}