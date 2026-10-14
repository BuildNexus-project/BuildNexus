using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>POST /api/auth/register</c>. Every field is required, and the
/// column limits in <c>users</c> are mirrored here so bad input is rejected
/// before it reaches MySQL. New accounts are always Clients; only an
/// administrator can change their role afterwards.
/// </summary>
public class RegisterRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 150 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Required contact number, using the profile's phone number format.</summary>
    [Required(ErrorMessage = "Phone number is required.")]
    [StringLength(30, ErrorMessage = "Phone number must not exceed 30 characters.")]
    [RegularExpression(@"^\s*(?:07[0-9]{8}|\+947[0-9]{8})\s*$",
        ErrorMessage = "Invalid mobile number")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Upper bound is there to cap the cost of hashing an arbitrarily long input.
    /// </summary>
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$",
        ErrorMessage = "Password must contain at least one letter and one digit.")]
    public string Password { get; set; } = string.Empty;
}
