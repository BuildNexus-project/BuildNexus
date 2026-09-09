using BuildNexus.DesignService.Services;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// An <see cref="IRevisionRequestNotifier"/> that records what it was asked to
/// send, standing in for the real HTTP lookup and SMTP send so the controller
/// suite needs neither.
/// </summary>
public sealed class FakeRevisionRequestNotifier : IRevisionRequestNotifier
{
    /// <summary>What the controller last asked to be sent, or <c>null</c> if nothing has.</summary>
    public (Guid ArchitectId, string DisplayName, string Comment)? LastNotification { get; private set; }

    /// <summary>Thrown from <see cref="NotifyAsync"/> when set, to exercise the failure path.</summary>
    public Exception? ThrowOnNotify { get; set; }

    public Task NotifyAsync(
        Guid architectId,
        string documentDisplayName,
        string comment,
        CancellationToken cancellationToken = default)
    {
        LastNotification = (architectId, documentDisplayName, comment);

        if (ThrowOnNotify is { } exception)
        {
            throw exception;
        }

        return Task.CompletedTask;
    }
}
