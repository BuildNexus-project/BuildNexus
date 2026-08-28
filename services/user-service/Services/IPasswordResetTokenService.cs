namespace BuildNexus.UserService.Services;

/// <summary>A freshly minted reset token and everything needed to record it.</summary>
/// <param name="Token">
/// The secret that goes in the email. This is the only moment it exists in
/// readable form — it is never stored and never logged.
/// </param>
/// <param name="TokenHash">What goes in <c>password_reset_tokens.token_hash</c>.</param>
/// <param name="ExpiresAtUtc">When the link stops working.</param>
public record MintedResetToken(string Token, string TokenHash, DateTime ExpiresAtUtc);

/// <summary>
/// Mints password reset tokens and hashes a redeemed one for lookup.
/// </summary>
public interface IPasswordResetTokenService
{
    /// <summary>
    /// Creates a new random token, expiring the configured window after
    /// <paramref name="issuedAtUtc"/>.
    /// </summary>
    MintedResetToken Mint(DateTime issuedAtUtc);

    /// <summary>
    /// Hashes a token handed back by a caller, so it can be looked up against
    /// the stored hashes. Same algorithm as <see cref="Mint"/> uses.
    /// </summary>
    string HashToken(string token);
}
