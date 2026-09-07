using System.ComponentModel.DataAnnotations;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// Payload for <c>PUT /api/projects/{id}/architect</c> — the account to put in
/// the project's Architect slot.
/// </summary>
/// <remarks>
/// The id only. Who is making the assignment comes from the caller's own token,
/// and the project is named by the route.
/// <para>
/// Nullable so an omitted id is a validation failure rather than binding
/// silently as <c>Guid.Empty</c> — "assign somebody" is not a thing to do by
/// accident. The account must actually hold the Architect role; that is checked
/// against the User Service, not here, since a data annotation cannot know it.
/// </para>
/// </remarks>
public class AssignArchitectRequest
{
    [Required(ErrorMessage = "An architect id is required.")]
    public Guid? ArchitectId { get; set; }
}
