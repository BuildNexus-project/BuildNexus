using System.ComponentModel.DataAnnotations;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// What a Project Manager submits to move a milestone along (US-12 AC-2).
/// </summary>
/// <remarks>
/// <see cref="Status"/> is a <em>nullable</em> enum on purpose. A plain
/// <c>MilestoneStatus</c> value type is never "missing" — an absent field
/// binds to its default (<c>NotStarted</c>) and <c>[Required]</c> on a value
/// type never fires, which would let <c>PATCH … {}</c> silently reset a
/// Completed milestone. Nullable makes <c>null</c> represent "the caller
/// didn't send a status", and <c>[Required]</c> then rejects it as a 400.
/// The integer wire form is refused elsewhere:
/// <c>JsonStringEnumConverter(allowIntegerValues: false)</c> in
/// <c>Program.cs</c> means only the three string names bind.
/// </remarks>
public class UpdateMilestoneStatusRequest
{
    [Required(ErrorMessage = "A status is required.")]
    [EnumDataType(typeof(MilestoneStatus))]
    public MilestoneStatus? Status { get; set; }
}