using System.ComponentModel.DataAnnotations;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Contracts;

/// <summary>
/// Payload for <c>PATCH /api/projects/{id}/status</c> — the status the caller
/// wants the project moved to.
/// </summary>
/// <remarks>
/// The status only. The project is named by the route, and who is making the
/// change comes from the caller's own token — so a request cannot claim the
/// move was made by somebody else, which is the whole value of an audit trail.
/// <para>
/// Carried as the status name rather than the enum's number: the name is what
/// the database stores and what every other response sends, and a payload of
/// <c>2</c> would silently mean something different if a value were ever
/// inserted into the middle of the enum.
/// </para>
/// </remarks>
public class UpdateProjectStatusRequest
{
    /// <summary>
    /// The new status, spelled exactly as <see cref="ProjectStatus"/> spells it
    /// — for example <c>DesignApproved</c>.
    /// </summary>
    /// <remarks>
    /// Whether the move is <em>allowed</em> is not a question this attribute
    /// can answer: it depends on the status the project currently holds, which
    /// is only known once the row has been read.
    /// <see cref="ProjectStatusTransitions"/> decides that.
    /// </remarks>
    [Required(ErrorMessage = "The new status is required.")]
    [EnumDataType(typeof(ProjectStatus), ErrorMessage = "That is not a project status.")]
    public string Status { get; set; } = string.Empty;
}
