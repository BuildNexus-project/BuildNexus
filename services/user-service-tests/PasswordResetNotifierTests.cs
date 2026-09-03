using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Models;
using BuildNexus.UserService.Services;
using Microsoft.Extensions.Options;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The email a reset request produces: who it goes to, and whether the link in
/// it actually works. The sender is a stand-in, so nothing is sent.
/// </summary>
public class PasswordResetNotifierTests
{
    private static readonly MintedResetToken Token = new(
        "a-reset-token",
        "the-hash-that-goes-in-the-database",
        new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Sends_the_link_to_the_address_registered_on_the_account()
    {
        // The AC is specific about this: the link goes to the registered email,
        // not to an address supplied with the request.
        var (notifier, sender) = NotifierFor();

        await notifier.SendResetLinkAsync(Recipient(), Token);

        Assert.Equal("ada@example.com", sender.Sent!.ToAddress);
        Assert.Equal("Ada Perera", sender.Sent.ToName);
    }

    [Fact]
    public async Task Puts_the_token_into_the_configured_url()
    {
        var (notifier, sender) = NotifierFor();

        await notifier.SendResetLinkAsync(Recipient(), Token);

        Assert.Contains(
            "https://app.example.com/reset-password?token=a-reset-token",
            sender.Sent!.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_no_placeholder_behind()
    {
        // A body still carrying {token} means the template and the substitution
        // disagreed, and the link in the user's inbox is dead.
        var (notifier, sender) = NotifierFor();

        await notifier.SendResetLinkAsync(Recipient(), Token);

        Assert.DoesNotContain(
            PasswordResetOptions.TokenPlaceholder, sender.Sent!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tells_the_reader_how_long_the_link_lasts()
    {
        var (notifier, sender) = NotifierFor(lifetimeMinutes: 30);

        await notifier.SendResetLinkAsync(Recipient(), Token);

        Assert.Contains("30 minutes", sender.Sent!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_what_the_email_is_for()
    {
        var (notifier, sender) = NotifierFor();

        await notifier.SendResetLinkAsync(Recipient(), Token);

        Assert.Contains("password", sender.Sent!.Subject, StringComparison.OrdinalIgnoreCase);
    }

    private static User Recipient() => new()
    {
        Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
        FullName = "Ada Perera",
        Email = "ada@example.com",
        Role = UserRole.Architect,
        IsActive = true
    };

    private static (PasswordResetNotifier Notifier, CapturingEmailSender Sender) NotifierFor(
        int lifetimeMinutes = 30)
    {
        var sender = new CapturingEmailSender();

        var notifier = new PasswordResetNotifier(sender, Options.Create(new PasswordResetOptions
        {
            TokenLifetimeMinutes = lifetimeMinutes,
            ResetUrlTemplate = "https://app.example.com/reset-password?token={token}"
        }));

        return (notifier, sender);
    }

    /// <summary>Keeps the message instead of sending it.</summary>
    private class CapturingEmailSender : IEmailSender
    {
        public EmailMessage? Sent { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent = message;
            return Task.CompletedTask;
        }
    }
}
