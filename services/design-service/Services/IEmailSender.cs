namespace BuildNexus.DesignService.Services;

/// <summary>One outbound plain-text email.</summary>
/// <param name="ToAddress">Recipient address.</param>
/// <param name="ToName">Recipient display name.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">Plain-text body.</param>
public record EmailMessage(string ToAddress, string ToName, string Subject, string Body);

/// <summary>
/// Delivers an email. The implementation in use depends on whether an SMTP host
/// is configured — see the registration in <c>Program.cs</c>.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends <paramref name="message"/>.</summary>
    /// <exception cref="Exception">
    /// Delivery failed. The caller decides what that means for it — a revision
    /// request that already succeeded is not undone by a notification that
    /// could not be sent.
    /// </exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
