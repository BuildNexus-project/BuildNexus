namespace BuildNexus.UserService.Services;

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
    /// Delivery failed. Callers decide what that means for them; the password
    /// reset endpoint treats it as a failure the user should be told about,
    /// since a link that was never sent will never arrive.
    /// </exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
