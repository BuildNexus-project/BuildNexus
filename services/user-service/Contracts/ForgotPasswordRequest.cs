using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>POST /api/auth/forgot-password</c>.
/// </summary>
/// <remarks>
/// Only the address is asked for — a user who has forgotten their password has
/// nothing else to prove who they are with. The endpoint answers the same way
/// whether or not it is registered, so a well-formed address here never reveals
/// whether an account exists.
/// </remarks>
public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;
}
