using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>POST /api/auth/login</c>.
/// </summary>
/// <remarks>
/// Only presence is validated. Applying the registration password rules here
/// would let a caller tell a badly-formed password apart from a wrong one.
/// </remarks>
public class LoginRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;
}
