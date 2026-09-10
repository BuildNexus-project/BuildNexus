namespace BuildNexus.UserService.Contracts;

/// <summary>
/// A user as returned to another BuildNexus service, not to a browser.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="UserResponse"/>: a caller here is
/// looking someone up to notify or display them, never to make an
/// authorization decision, so role, phone number and contact address are left
/// out rather than handed to every service that ever needs a name.
/// </remarks>
public class InternalUserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
