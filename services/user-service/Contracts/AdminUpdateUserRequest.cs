using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>PUT /api/users/{id}</c>: the three fields an administrator
/// may change on someone else's account. The column limits in <c>users</c> are
/// mirrored here so bad input is rejected before it reaches MySQL.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="UpdateProfileRequest"/>, from the other side of
/// the same line: a user maintains their own name and contact details, while
/// the email — the login identity — and the role — the authorisation boundary —
/// are an administrator's to move.
/// <para>
/// A replacement rather than a patch: all three are required, and all three are
/// written. There is nothing to omit, so there is no "unset or unchanged?"
/// ambiguity to resolve. Contact details are absent because they are the user's
/// own, and the password is absent because nobody, administrator included, gets
/// to set another person's password — that is what the reset link is for.
/// </para>
/// </remarks>
public class AdminUpdateUserRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 150 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Any of the four roles, <c>Admin</c> included — promoting an administrator
    /// is exactly what this endpoint is for, and is the one path to the role
    /// that self-service registration refuses.
    /// </summary>
    [Required(ErrorMessage = "Role is required.")]
    [PlatformRole]
    public string Role { get; set; } = string.Empty;
}
