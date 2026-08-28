using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Services;

/// <summary>
/// Emails a user the link that lets them choose a new password.
/// </summary>
public interface IPasswordResetNotifier
{
    /// <summary>
    /// Sends <paramref name="user"/> a reset link carrying
    /// <paramref name="token"/> and says when it stops working.
    /// </summary>
    Task SendResetLinkAsync(
        User user,
        MintedResetToken token,
        CancellationToken cancellationToken = default);
}
