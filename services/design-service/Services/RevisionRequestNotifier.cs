using BuildNexus.DesignService.Models;
using BuildNexus.DesignService.Users;

namespace BuildNexus.DesignService.Services;

/// <summary>
/// Builds the revision-request email and hands it to the configured
/// <see cref="IEmailSender"/>.
/// </summary>
/// <remarks>
/// Kept out of the controller so the wording and the lookup are one testable
/// thing, the same reasoning <c>user-service</c>'s <c>PasswordResetNotifier</c>
/// follows.
/// </remarks>
public class RevisionRequestNotifier : IRevisionRequestNotifier
{
    private const string Subject = "A revision was requested on your design";

    private readonly IInternalUserClient _userClient;
    private readonly IEmailSender _emailSender;

    public RevisionRequestNotifier(IInternalUserClient userClient, IEmailSender emailSender)
    {
        _userClient = userClient;
        _emailSender = emailSender;
    }

    public async Task NotifyAsync(
        Guid architectId,
        string documentDisplayName,
        string comment,
        CancellationToken cancellationToken = default)
    {
        var user = await _userClient.GetUserAsync(architectId, cancellationToken);

        if (user.Outcome != InternalUserLookupOutcome.Found)
        {
            throw new InvalidOperationException(
                $"Could not look up Architect {architectId} to notify them: "
                + $"the User Service lookup returned {user.Outcome}.");
        }

        var body =
            $"""
             Hello {user.FullName},

             A revision has been requested on {documentDisplayName}:

             {comment}

             — BuildNexus
             """;

        await _emailSender.SendAsync(
            new EmailMessage(user.Email!, user.FullName!, Subject, body), cancellationToken);
    }
}
