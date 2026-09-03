namespace BuildNexus.UserService.Data;

/// <summary>
/// Raised when an insert loses the race against the <c>uq_users_email</c> unique
/// constraint, i.e. the email was registered between the check and the insert.
/// </summary>
public class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string email)
        : base($"Email '{email}' is already registered.")
    {
        Email = email;
    }

    public string Email { get; }
}
