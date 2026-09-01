using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Controllers;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using BuildNexus.UserService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// What <c>POST /api/auth/forgot-password</c> and
/// <c>POST /api/auth/reset-password</c> do once a payload has passed
/// validation — the three US-04 acceptance criteria, one by one.
/// </summary>
/// <remarks>
/// The repositories and the email sender are stand-ins, so these run without a
/// database or a mail server. The password hasher and the token service are the
/// real ones: whether the old password still works afterwards is the point of
/// the story, and a stubbed hasher could not answer it.
/// </remarks>
public class AuthControllerPasswordResetTests
{
    private const string OldPassword = "OldPassw0rd";
    private const string NewPassword = "NewPassw0rd";

    // ----- AC 1: a reset request emails a time-limited token ----------------

    [Fact]
    public async Task Requesting_a_reset_emails_a_link_to_the_registered_address()
    {
        var (controller, world) = ControllerFor(ActiveUser());

        var result = await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal("ada@example.com", world.Sender.Sent!.ToAddress);
    }

    [Fact]
    public async Task The_emailed_token_is_the_one_recorded_against_the_account()
    {
        // A stored hash that the emailed token does not hash to would mean a
        // link nobody can redeem.
        var (controller, world) = ControllerFor(ActiveUser());

        await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);

        var stored = Assert.Single(world.Tokens.Rows);
        Assert.Equal(stored.TokenHash, world.TokenService.HashToken(world.Sender.EmailedToken!));
        Assert.Equal(UserId, stored.UserId);
    }

    [Fact]
    public async Task The_raw_token_is_never_stored()
    {
        var (controller, world) = ControllerFor(ActiveUser());

        await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);

        // The database holds a hash. Holding the token itself would make a copy
        // of the table a set of working links into every account.
        Assert.NotEqual(world.Sender.EmailedToken, Assert.Single(world.Tokens.Rows).TokenHash);
    }

    [Fact]
    public async Task Looking_up_the_account_ignores_the_case_the_address_was_typed_in()
    {
        var (controller, world) = ControllerFor(ActiveUser());

        await controller.ForgotPassword(Request("  Ada@Example.COM "), CancellationToken.None);

        Assert.NotNull(world.Sender.Sent);
    }

    // ----- AC 2: the token expires after a set window -----------------------

    [Fact]
    public async Task The_link_expires_within_the_configured_window()
    {
        var (controller, world) = ControllerFor(ActiveUser());

        var before = DateTime.UtcNow;
        await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);
        var after = DateTime.UtcNow;

        var stored = Assert.Single(world.Tokens.Rows);
        Assert.InRange(
            stored.ExpiresAtUtc,
            before.AddMinutes(LifetimeMinutes),
            after.AddMinutes(LifetimeMinutes));
    }

    [Fact]
    public async Task An_expired_link_is_refused_and_leaves_the_password_alone()
    {
        var user = ActiveUser();
        var (controller, world) = ControllerFor(user);
        var token = await IssueLinkAsync(controller, world);

        // One second past the window is past it.
        world.Tokens.Rows[0].ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);

        var result = await controller.ResetPassword(ResetRequest(token));

        AssertRefused(result);
        AssertPasswordStillIs(OldPassword, user);
    }

    [Fact]
    public async Task A_link_inside_the_window_is_accepted()
    {
        var (controller, world) = ControllerFor(ActiveUser());
        var token = await IssueLinkAsync(controller, world);

        world.Tokens.Rows[0].ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30);

        Assert.IsType<OkObjectResult>(await controller.ResetPassword(ResetRequest(token)));
    }

    // ----- AC 3: the old password stops working once the reset completes -----

    [Fact]
    public async Task Completing_the_reset_retires_the_old_password()
    {
        var user = ActiveUser();
        var (controller, world) = ControllerFor(user);
        var token = await IssueLinkAsync(controller, world);

        var result = await controller.ResetPassword(ResetRequest(token));

        Assert.IsType<OkObjectResult>(result);
        // The hash was overwritten, so there is nothing left for the old
        // password to verify against.
        AssertPasswordStillIs(NewPassword, user);
        Assert.False(new Pbkdf2PasswordHasher().Verify(OldPassword, user.PasswordHash));
    }

    [Fact]
    public async Task Completing_the_reset_spends_the_link()
    {
        var (controller, world) = ControllerFor(ActiveUser());
        var token = await IssueLinkAsync(controller, world);

        await controller.ResetPassword(ResetRequest(token));

        Assert.NotNull(world.Tokens.Rows[0].ConsumedAtUtc);
    }

    [Fact]
    public async Task The_same_link_cannot_be_used_twice()
    {
        var user = ActiveUser();
        var (controller, world) = ControllerFor(user);
        var token = await IssueLinkAsync(controller, world);

        Assert.IsType<OkObjectResult>(await controller.ResetPassword(ResetRequest(token)));

        // A second attempt with a third password must change nothing.
        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Token = token,
            NewPassword = "Later0ne!"
        });

        AssertRefused(result);
        AssertPasswordStillIs(NewPassword, user);
    }

    [Fact]
    public async Task Requesting_a_new_link_kills_the_one_before_it()
    {
        // Asking again because the first email went astray must not leave two
        // ways into the account outstanding.
        var user = ActiveUser();
        var (controller, world) = ControllerFor(user);

        var firstToken = await IssueLinkAsync(controller, world);
        var secondToken = await IssueLinkAsync(controller, world);

        AssertRefused(await controller.ResetPassword(ResetRequest(firstToken)));
        AssertPasswordStillIs(OldPassword, user);

        Assert.IsType<OkObjectResult>(await controller.ResetPassword(ResetRequest(secondToken)));
    }

    // ----- Refusals, and saying nothing about who has an account ------------

    [Fact]
    public async Task An_unknown_address_is_answered_exactly_like_a_registered_one()
    {
        var (knownController, known) = ControllerFor(ActiveUser());
        var (unknownController, unknown) = ControllerFor(storedUser: null);

        var knownResult = await knownController.ForgotPassword(
            Request("ada@example.com"), CancellationToken.None);
        var unknownResult = await unknownController.ForgotPassword(
            Request("nobody@example.com"), CancellationToken.None);

        // Same status and same wording: this endpoint is open to anyone, so a
        // difference here would be a way to find out who is registered.
        Assert.Equal(AcceptedBody(knownResult).Message, AcceptedBody(unknownResult).Message);

        // And nothing was actually issued for the address with no account.
        Assert.Empty(unknown.Tokens.Rows);
        Assert.Null(unknown.Sender.Sent);
        Assert.NotNull(known.Sender.Sent);
    }

    [Fact]
    public async Task A_deactivated_account_gets_no_link()
    {
        // A reset link would otherwise be a way back in for an account somebody
        // deliberately switched off.
        var deactivated = ActiveUser();
        deactivated.IsActive = false;

        var (controller, world) = ControllerFor(deactivated);

        var result = await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(world.Tokens.Rows);
        Assert.Null(world.Sender.Sent);
    }

    [Fact]
    public async Task A_failed_send_is_still_answered_the_same_way()
    {
        var (controller, world) = ControllerFor(ActiveUser());
        world.Sender.FailNextSend = true;

        var result = await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);

        // Reporting the failure would only ever happen for an address that does
        // have an account — which is precisely what must not be revealed.
        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task A_token_nobody_issued_is_refused()
    {
        var user = ActiveUser();
        var (controller, _) = ControllerFor(user);

        var result = await controller.ResetPassword(ResetRequest("a-token-from-nowhere"));

        AssertRefused(result);
        AssertPasswordStillIs(OldPassword, user);
    }

    [Fact]
    public async Task A_link_for_an_account_deactivated_since_it_was_sent_is_refused()
    {
        var user = ActiveUser();
        var (controller, world) = ControllerFor(user);
        var token = await IssueLinkAsync(controller, world);

        user.IsActive = false;

        AssertRefused(await controller.ResetPassword(ResetRequest(token)));
        AssertPasswordStillIs(OldPassword, user);
    }

    [Fact]
    public async Task Every_refusal_gives_the_same_reason()
    {
        // Unknown, expired and already-used all answer alike: telling them apart
        // would tell a stranger which tokens once existed.
        var (controller, world) = ControllerFor(ActiveUser());

        var expiredToken = await IssueLinkAsync(controller, world);
        world.Tokens.Rows[0].ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);

        var expired = RefusalBody(await controller.ResetPassword(ResetRequest(expiredToken)));
        var unknown = RefusalBody(await controller.ResetPassword(ResetRequest("a-token-from-nowhere")));

        Assert.Equal(expired.Title, unknown.Title);
        Assert.Equal(expired.Detail, unknown.Detail);
    }

    // ----- Fixtures --------------------------------------------------------

    private const int LifetimeMinutes = 30;

    private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static User ActiveUser() => new()
    {
        Id = UserId,
        FullName = "Ada Perera",
        Email = "ada@example.com",
        PasswordHash = new Pbkdf2PasswordHasher().Hash(OldPassword),
        Role = UserRole.Architect,
        IsActive = true,
        CreatedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    private static ForgotPasswordRequest Request(string email) => new() { Email = email };

    private static ResetPasswordRequest ResetRequest(string token) =>
        new() { Token = token, NewPassword = NewPassword };

    /// <summary>
    /// Runs a real reset request and returns the token that reached the email,
    /// which is the only place it ever appears.
    /// </summary>
    private static async Task<string> IssueLinkAsync(AuthController controller, TestWorld world)
    {
        await controller.ForgotPassword(Request("ada@example.com"), CancellationToken.None);

        return world.Sender.EmailedToken!;
    }

    private static void AssertRefused(IActionResult result) =>
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalBody(result).Status);

    private static void AssertPasswordStillIs(string password, User user) =>
        Assert.True(new Pbkdf2PasswordHasher().Verify(password, user.PasswordHash));

    private static ProblemDetails RefusalBody(IActionResult result) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);

    private static MessageResponse AcceptedBody(IActionResult result) =>
        Assert.IsType<MessageResponse>(Assert.IsType<AcceptedResult>(result).Value);

    /// <summary>Everything standing in for the world outside the controller.</summary>
    private record TestWorld(
        StubUserRepository Users,
        StubPasswordResetTokenRepository Tokens,
        TokenCapturingSender Sender,
        IPasswordResetTokenService TokenService);

    private static (AuthController Controller, TestWorld World) ControllerFor(User? storedUser)
    {
        var users = new StubUserRepository(storedUser);
        var tokens = new StubPasswordResetTokenRepository();
        var sender = new TokenCapturingSender();

        var options = Options.Create(new PasswordResetOptions
        {
            TokenLifetimeMinutes = LifetimeMinutes,
            // The sender reads the token back out of the link, so this template
            // is what makes the emailed token visible to the tests.
            ResetUrlTemplate = TokenCapturingSender.UrlTemplate
        });

        var tokenService = new PasswordResetTokenService(options);

        var controller = new AuthController(
            users,
            tokens,
            new Pbkdf2PasswordHasher(),
            new UnusedJwtTokenService(),
            tokenService,
            new PasswordResetNotifier(sender, options),
            NullLogger<AuthController>.Instance);

        return (controller, new TestWorld(users, tokens, sender, tokenService));
    }

    /// <summary>
    /// Keeps the email instead of sending it, and pulls the token back out of
    /// the link — the same thing a user does when they click it.
    /// </summary>
    private class TokenCapturingSender : IEmailSender
    {
        public const string UrlTemplate = "https://app.example.com/reset-password?token={token}";

        private const string LinkPrefix = "https://app.example.com/reset-password?token=";

        /// <summary>Set to reproduce a mail server that will not take the message.</summary>
        public bool FailNextSend { get; set; }

        public EmailMessage? Sent { get; private set; }

        /// <summary>The token as it appeared in the emailed link.</summary>
        public string? EmailedToken { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (FailNextSend)
            {
                FailNextSend = false;
                throw new InvalidOperationException("The mail server refused the message.");
            }

            Sent = message;

            var start = message.Body.IndexOf(LinkPrefix, StringComparison.Ordinal) + LinkPrefix.Length;
            var end = message.Body.IndexOfAny([' ', '\r', '\n'], start);
            EmailedToken = message.Body[start..end];

            return Task.CompletedTask;
        }
    }

    /// <summary>Holds one user in memory, and lets the tests see what was written to it.</summary>
    private class StubUserRepository : IUserRepository
    {
        private readonly User? _user;

        public StubUserRepository(User? user) => _user = user;

        public Task<User?> GetByEmailAsync(string email) =>
            Task.FromResult(_user is not null && _user.Email == email ? _user : null);

        public Task<User?> GetByIdAsync(Guid id) =>
            Task.FromResult(_user is not null && _user.Id == id ? _user : null);

        public Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, DateTime updatedAtUtc)
        {
            if (_user is null || _user.Id != userId)
            {
                return Task.FromResult(false);
            }

            _user.PasswordHash = passwordHash;
            _user.UpdatedAt = updatedAtUtc;

            return Task.FromResult(true);
        }

        // Not what these tests are about — see the other test classes.
        public Task<bool> EmailExistsAsync(string email) => Task.FromResult(false);

        public Task<bool> AdminExistsAsync() => Task.FromResult(true);

        public Task<PagedResult<User>> ListPageAsync(UserRole? role, int page, int pageSize) =>
            Task.FromResult(new PagedResult<User>());

        public Task<IReadOnlyList<User>> ListActiveByRolesAsync(IReadOnlyCollection<UserRole> roles) =>
            Task.FromResult<IReadOnlyList<User>>([]);

        public Task InsertAsync(User user) => Task.CompletedTask;

        public Task<bool> UpdateProfileAsync(User user) => Task.FromResult(true);

        public Task<bool> UpdateAccountAsync(User user) => Task.FromResult(true);

        public Task<bool> SetActiveAsync(Guid userId, bool isActive, DateTime updatedAtUtc) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// An in-memory <c>password_reset_tokens</c>, enforcing the same
    /// "only once, only while outstanding" rule the real UPDATEs do.
    /// </summary>
    private class StubPasswordResetTokenRepository : IPasswordResetTokenRepository
    {
        public List<PasswordResetToken> Rows { get; } = [];

        public Task InsertAsync(PasswordResetToken token)
        {
            Rows.Add(token);
            return Task.CompletedTask;
        }

        public Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.TokenHash == tokenHash));

        public Task<bool> MarkConsumedAsync(Guid id, DateTime consumedAtUtc)
        {
            var row = Rows.FirstOrDefault(candidate => candidate.Id == id && candidate.ConsumedAtUtc is null);

            if (row is null)
            {
                return Task.FromResult(false);
            }

            row.ConsumedAtUtc = consumedAtUtc;
            return Task.FromResult(true);
        }

        public Task<int> InvalidateOutstandingForUserAsync(Guid userId, DateTime consumedAtUtc)
        {
            var outstanding = Rows
                .Where(row => row.UserId == userId && row.ConsumedAtUtc is null)
                .ToList();

            foreach (var row in outstanding)
            {
                row.ConsumedAtUtc = consumedAtUtc;
            }

            return Task.FromResult(outstanding.Count);
        }
    }

    /// <summary>
    /// The controller takes one, but resetting a password never issues a token —
    /// the user signs in afterwards like anyone else.
    /// </summary>
    private class UnusedJwtTokenService : IJwtTokenService
    {
        public IssuedToken CreateAccessToken(User user) =>
            throw new InvalidOperationException("Password reset must not issue an access token.");
    }
}
