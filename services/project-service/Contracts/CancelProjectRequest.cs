using System.ComponentModel.DataAnnotations;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// Payload for <c>POST /api/projects/{id}/cancellation</c> — the reason the
/// project is being closed out.
/// </summary>
/// <remarks>
/// The reason only. Who is cancelling comes from the caller's token, and the
/// project is named by the route. A reason is required: US-08 asks for one to
/// be recorded, and a cancellation with no explanation is the "left stale"
/// state the story is closing.
/// </remarks>
public class CancelProjectRequest
{
    [Required(ErrorMessage = "A reason for cancelling is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "The reason must be between 1 and 500 characters.")]
    public string Reason { get; set; } = string.Empty;
}
