namespace BuildNexus.DesignService.Models;

/// <summary>What the User Service said when asked to look up an account by id.</summary>
public enum InternalUserLookupOutcome
{
    /// <summary>The account exists — <see cref="InternalUserLookup.FullName"/> and <see cref="InternalUserLookup.Email"/> are set.</summary>
    Found,

    /// <summary>No account with that id.</summary>
    NotFound,

    /// <summary>The User Service could not be reached or did not answer usefully.</summary>
    Unavailable
}

/// <summary>
/// The answer to "who is this account", as the User Service gave it — relayed
/// rather than cached, since that service owns the fact.
/// </summary>
public sealed class InternalUserLookup
{
    private InternalUserLookup(InternalUserLookupOutcome outcome, string? fullName, string? email)
    {
        Outcome = outcome;
        FullName = fullName;
        Email = email;
    }

    public InternalUserLookupOutcome Outcome { get; }

    /// <summary>Meaningful only when <see cref="Outcome"/> is <see cref="InternalUserLookupOutcome.Found"/>.</summary>
    public string? FullName { get; }

    /// <summary>Meaningful only when <see cref="Outcome"/> is <see cref="InternalUserLookupOutcome.Found"/>.</summary>
    public string? Email { get; }

    public static InternalUserLookup Found(string fullName, string email) =>
        new(InternalUserLookupOutcome.Found, fullName, email);

    public static readonly InternalUserLookup NotFound = new(InternalUserLookupOutcome.NotFound, null, null);

    public static readonly InternalUserLookup Unavailable = new(InternalUserLookupOutcome.Unavailable, null, null);
}
