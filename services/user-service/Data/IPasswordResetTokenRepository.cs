using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Data;

/// <summary>
/// Data access for the <c>password_reset_tokens</c> table.
/// </summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>Records a newly issued reset link.</summary>
    Task InsertAsync(PasswordResetToken token);

    /// <summary>
    /// Returns the link with this token hash, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// Expired and already-redeemed rows are returned too. Whether a link may
    /// still be used is decided by the caller against a single clock reading,
    /// rather than by a WHERE clause reading its own.
    /// </remarks>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash);

    /// <summary>
    /// Marks one outstanding link as redeemed.
    /// </summary>
    /// <returns>
    /// <c>false</c> when the row was already redeemed — two requests carrying
    /// the same link raced, and this one lost. The winner is the only one that
    /// gets to change the password.
    /// </returns>
    Task<bool> MarkConsumedAsync(Guid id, DateTime consumedAtUtc);

    /// <summary>
    /// Spends every link still outstanding for a user, so that only the newest
    /// one works.
    /// </summary>
    /// <returns>How many links were invalidated.</returns>
    Task<int> InvalidateOutstandingForUserAsync(Guid userId, DateTime consumedAtUtc);
}
