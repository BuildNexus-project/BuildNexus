using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>POST /api/auth/reset-password</c>: the token from the emailed
/// link, and the password to replace the forgotten one with.
/// </summary>
public class ResetPasswordRequest
{
    /// <summary>
    /// The token out of the emailed link, exactly as it was received.
    /// </summary>
    /// <remarks>
    /// The length bound matches what the service mints — 32 random bytes,
    /// base64url — with room to spare, so a caller cannot make the service hash
    /// a megabyte of input looking for a match.
    /// </remarks>
    [Required(ErrorMessage = "Reset token is required.")]
    [StringLength(256, MinimumLength = 16, ErrorMessage = "Reset token is not a valid token.")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The new password. Same rules registration applies, so a password chosen
    /// here is never weaker than one chosen when signing up.
    /// </summary>
    [Required(ErrorMessage = "New password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$",
        ErrorMessage = "Password must contain at least one letter and one digit.")]
    public string NewPassword { get; set; } = string.Empty;
}
