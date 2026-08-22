using System.ComponentModel.DataAnnotations;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Payload for <c>PUT /api/users/me</c>. Carries only the fields a user may edit
/// on their own account, with the column limits in <c>users</c> mirrored here so
/// bad input is rejected before it reaches MySQL.
/// </summary>
/// <remarks>
/// <see cref="Email"/> and <see cref="Role"/> are declared but never applied.
/// The team's decision for US-02 is that neither is self-editable: the email is
/// the login identity and the role is the authorisation boundary, so changing
/// either is an administrator's job. They are accepted by the binder purely so
/// an attempt can be refused with a clear message — silently discarding them
/// would leave the caller believing the change had been saved.
/// </remarks>
public class UpdateProfileRequest : IValidatableObject
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 150 characters.")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Optional. Permissive about formatting — spaces, brackets and hyphens are
    /// all common ways to write the same number — but strict about content, and
    /// long enough to be a real number rather than a stray digit.
    /// </summary>
    /// <remarks>
    /// An empty string is left to pass here and is stored as <c>NULL</c>: that is
    /// how the profile form clears a field it had previously filled in.
    /// </remarks>
    [StringLength(30, ErrorMessage = "Phone number must not exceed 30 characters.")]
    [RegularExpression(@"^\+?[0-9][0-9 ()\-]{6,}$",
        ErrorMessage = "Phone number must be at least 7 characters and may contain only digits, spaces, brackets, hyphens and a leading +.")]
    public string? PhoneNumber { get; set; }

    /// <summary>Optional postal or site address. Cleared the same way as the phone number.</summary>
    [StringLength(255, ErrorMessage = "Contact address must not exceed 255 characters.")]
    public string? ContactAddress { get; set; }

    /// <summary>Never applied — see the remarks on this class.</summary>
    public string? Email { get; set; }

    /// <summary>Never applied — see the remarks on this class.</summary>
    public string? Role { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Absent is the expected case. Anything else — including a value that
        // happens to match what is already stored — is refused, so there is one
        // rule to explain rather than "sometimes it works".
        if (Email is not null)
        {
            yield return new ValidationResult(
                "Email cannot be changed here. Ask an administrator to change it for you.",
                [nameof(Email)]);
        }

        if (Role is not null)
        {
            yield return new ValidationResult(
                "Role cannot be changed here. Ask an administrator to change it for you.",
                [nameof(Role)]);
        }
    }
}
