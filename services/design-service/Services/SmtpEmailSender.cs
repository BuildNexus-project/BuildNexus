using System.Net;
using System.Net.Mail;
using BuildNexus.DesignService.Configuration;
using Microsoft.Extensions.Options;

namespace BuildNexus.DesignService.Services;

/// <summary>
/// Sends email through the configured SMTP server.
/// </summary>
/// <remarks>
/// Used whenever <see cref="EmailOptions.SmtpHost"/> is set; otherwise
/// <see cref="LoggingEmailSender"/> takes its place. The same shape as
/// <c>user-service</c>'s identically-named sender.
/// </remarks>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        // A client per send: SmtpClient is not safe to share across requests,
        // and a revision-request notification is rare enough that pooling one
        // would buy nothing.
        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.UseStartTls
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };

        mail.To.Add(new MailAddress(message.ToAddress, message.ToName));

        await client.SendMailAsync(mail, cancellationToken);

        // The address, never the body: a revision comment can be candid, and
        // the log is not where it belongs.
        _logger.LogInformation("Sent \"{Subject}\" to {Recipient}.", message.Subject, message.ToAddress);
    }
}
