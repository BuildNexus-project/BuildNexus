namespace BuildNexus.DesignService.Services;

/// <summary>
/// Writes each email to the log instead of sending it.
/// </summary>
/// <remarks>
/// Stands in for <see cref="SmtpEmailSender"/> whenever no SMTP host is
/// configured, which is the normal local setup: a revision-request
/// notification is read out of the service's console rather than an inbox.
/// The same shape as <c>user-service</c>'s identically-named sender.
/// </remarks>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "No SMTP host configured — email not sent. To: {Recipient}. Subject: {Subject}.{NewLine}{Body}",
            message.ToAddress,
            message.Subject,
            Environment.NewLine,
            message.Body);

        return Task.CompletedTask;
    }
}
