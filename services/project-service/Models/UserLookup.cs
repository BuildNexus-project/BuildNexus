namespace BuildNexus.ProjectService.Models;

/// <summary>
/// What the User Service said when asked about an account.
/// </summary>
public enum UserLookupOutcome
{
    /// <summary>The account exists; <see cref="UserLookup.Role"/> is its role.</summary>
    Found,

    /// <summary>No account with that id.</summary>
    NotFound,

    /// <summary>The User Service could not be reached or did not answer usefully.</summary>
    Unavailable
}

/// <summary>
/// The answer to "does this account exist, and what role does it hold", as the
/// User Service gave it — relayed rather than re-decided, since that service
/// owns the fact.
/// </summary>
public sealed class UserLookup
{
    private UserLookup(UserLookupOutcome outcome, string? role)
    {
        Outcome = outcome;
        Role = role;
    }

    public UserLookupOutcome Outcome { get; }

    /// <summary>
    /// The account's role, as one of the four platform role names. Meaningful
    /// only when <see cref="Outcome"/> is <see cref="UserLookupOutcome.Found"/>.
    /// </summary>
    public string? Role { get; }

    /// <summary>Whether the account exists and holds exactly <paramref name="role"/>.</summary>
    public bool HasRole(string role) =>
        Outcome == UserLookupOutcome.Found && string.Equals(Role, role, StringComparison.Ordinal);

    public static UserLookup Found(string role) => new(UserLookupOutcome.Found, role);

    public static readonly UserLookup NotFound = new(UserLookupOutcome.NotFound, null);

    public static readonly UserLookup Unavailable = new(UserLookupOutcome.Unavailable, null);
}
