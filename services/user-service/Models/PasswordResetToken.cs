namespace BuildNexus.UserService.Models;

/// <summary>
/// A password reset link that has been emailed to a user, mapped by hand from
/// the <c>password_reset_tokens</c> table.
/// </summary>
/// <remarks>
/// The token itself only ever exists in the email. What is held here is its
/// hash, so this row cannot be turned back into a working link.
/// </remarks>
public class PasswordResetToken
{
    public Guid Id { get; set; }

    /// <summary>The account the link resets. Always a real <c>users.id</c>.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the emailed token, as lowercase hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>When the link stops working, whether or not it was ever opened.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// When the link was redeemed, or <c>null</c> while it is still outstanding.
    /// A redeemed link is spent: it cannot reset a password a second time.
    /// </summary>
    public DateTime? ConsumedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// True when this link can still be redeemed at <paramref name="asOfUtc"/> —
    /// not already spent, and not past its expiry.
    /// </summary>
    public bool IsRedeemableAt(DateTime asOfUtc) =>
        ConsumedAtUtc is null && ExpiresAtUtc > asOfUtc;
}
