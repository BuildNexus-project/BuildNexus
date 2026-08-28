using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Models;
using Microsoft.Extensions.Options;

namespace BuildNexus.UserService.Services;

/// <summary>
/// Builds the password reset email and hands it to the configured
/// <see cref="IEmailSender"/>.
/// </summary>
/// <remarks>
/// Kept out of the controller so the wording and the link are one testable
/// thing, and so the endpoint deals in "send the link" rather than in subjects
/// and bodies.
/// </remarks>
public class PasswordResetNotifier : IPasswordResetNotifier
{
    private const string Subject = "Reset your BuildNexus password";

    private readonly IEmailSender _emailSender;
    private readonly PasswordResetOptions _options;

    public PasswordResetNotifier(IEmailSender emailSender, IOptions<PasswordResetOptions> options)
    {
        _emailSender = emailSender;
        _options = options.Value;
    }

    public Task SendResetLinkAsync(
        User user,
        MintedResetToken token,
        CancellationToken cancellationToken = default)
    {
        // Not Uri.EscapeDataString: the token is base64url, whose alphabet is
        // already safe in a query string, and escaping it would only risk the
        // two sides disagreeing about what the token actually is.
        var resetUrl = _options.ResetUrlTemplate.Replace(
            PasswordResetOptions.TokenPlaceholder, token.Token, StringComparison.Ordinal);

        var body =
            $"""
             Hello {user.FullName},

             Someone asked to reset the password for your BuildNexus account. Open
             the link below to choose a new one:

             {resetUrl}

             The link stops working {_options.TokenLifetimeMinutes} minutes after it
             was requested, and can only be used once. Until you complete the reset
             your current password keeps working.

             If this was not you, no action is needed — nothing has changed.

             — BuildNexus
             """;

        return _emailSender.SendAsync(
            new EmailMessage(user.Email, user.FullName, Subject, body), cancellationToken);
    }
}
