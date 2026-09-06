using System.ComponentModel.DataAnnotations;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// Payload for <c>PUT /api/projects/{id}/project-manager</c> — the account to
/// put in the project's Project Manager slot.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="AssignArchitectRequest"/>. Nullable and
/// <c>[Required]</c> for the same reason, and the account must hold the
/// Project Manager role — checked against the User Service.
/// </remarks>
public class AssignProjectManagerRequest
{
    [Required(ErrorMessage = "A project manager id is required.")]
    public Guid? ProjectManagerId { get; set; }
}
