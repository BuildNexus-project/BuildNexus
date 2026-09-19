using System.ComponentModel.DataAnnotations;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// What a Project Manager submits to move a milestone along (US-12 AC-2).
/// </summary>
/// <remarks>
/// Bound as the enum directly, so a value outside <c>NotStarted</c>,
/// <c>InProgress</c> or <c>Completed</c> comes back as a
/// <c>400 Bad Request</c> with a field error before the controller runs —
/// System.Text.Json refuses the payload rather than snapping to a default.
/// </remarks>
public class UpdateMilestoneStatusRequest
{
    [Required(ErrorMessage = "A status is required.")]
    [EnumDataType(typeof(MilestoneStatus))]
    public MilestoneStatus Status { get; set; }
}