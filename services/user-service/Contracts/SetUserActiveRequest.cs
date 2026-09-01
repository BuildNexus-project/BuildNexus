using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>PATCH /api/users/{id}/status</c>: whether the account may be
/// signed in to.
/// </summary>
/// <remarks>
/// A flag rather than a <c>DELETE</c>. An account is the author of projects,
/// approvals and status changes across the platform, and removing the row would
/// orphan every one of them; withdrawing access leaves that history intact and
/// legible. It also means the same endpoint reinstates an account that was
/// deactivated by mistake, instead of that being a one-way door.
/// <para>
/// Nullable so an omitted flag is a validation failure rather than a silent
/// <c>false</c> — "deactivate" is not a thing to do by accident.
/// </para>
/// </remarks>
public class SetUserActiveRequest
{
    [Required(ErrorMessage = "Specify whether the account should be active.")]
    public bool? IsActive { get; set; }
}
