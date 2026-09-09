namespace BuildNexus.DesignService.Services;

/// <summary>Tells an Architect a Client asked for changes on their work (US-11).</summary>
public interface IRevisionRequestNotifier
{
    /// <summary>
    /// Looks up <paramref name="architectId"/> and emails them the comment.
    /// </summary>
    /// <exception cref="Exception">
    /// The lookup or the send failed. The caller decides what that means for
    /// it — the review decision has already been recorded, and a notification
    /// that could not be sent should not undo that.
    /// </exception>
    Task NotifyAsync(
        Guid architectId,
        string documentDisplayName,
        string comment,
        CancellationToken cancellationToken = default);
}
