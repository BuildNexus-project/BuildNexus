using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using BuildNexus.UserService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.UserService.Controllers;

/// <summary>
/// Registration, login and password reset.
/// </summary>
/// <remarks>
/// Every endpoint here is open to anonymous callers by design — they are how a
/// caller obtains a token in the first place, or gets back in when they cannot.
/// Every other endpoint in BuildNexus requires a bearer token and names the
/// roles allowed to call it.
/// </remarks>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    /// <summary>Returned for every login failure, whatever the underlying cause.</summary>
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    /// <summary>
    /// Returned for every reset request, registered address or not. The same
    /// wording either way is what stops the endpoint being used to find out
    /// which addresses have accounts.
    /// </summary>
    private const string ResetRequestedMessage =
        "If that email address has an account, a reset link is on its way. The link expires in 30 minutes.";

    /// <summary>
    /// Returned for every unusable reset link — unknown, expired, or already
    /// spent. Which of the three it was goes to the log, not to the caller.
    /// </summary>
    private const string InvalidResetLinkMessage =
        "This password reset link is no longer valid. Request a new one and try again.";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordResetTokenService _passwordResetTokenService;
    private readonly IPasswordResetNotifier _passwordResetNotifier;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IPasswordResetTokenService passwordResetTokenService,
        IPasswordResetNotifier passwordResetNotifier,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _passwordResetTokenService = passwordResetTokenService;
        _passwordResetNotifier = passwordResetNotifier;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new account. Allowed roles: anonymous.
    /// </summary>
    /// <response code="201">The account was created.</response>
    /// <response code="400">The payload failed validation.</response>
    /// <response code="409">The email is already registered.</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Self-service registration must never mint an Admin. The match is exact
        // and case-sensitive against the canonical role names, so "admin" is
        // rejected here rather than reaching ck_users_role as a raw SQL error.
        // Checked before hashing: no reason to spend 210k PBKDF2 iterations on a
        // request that is already refused.
        if (request.Role is not ("Client" or "Architect" or "ProjectManager"))
        {
            _logger.LogWarning("Registration rejected: role {Role} is not self-assignable.", request.Role);

            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Role"] = ["Role must be one of: Client, Architect, ProjectManager."]
            }));
        }

        var email = NormaliseEmail(request.Email);

        if (await _userRepository.EmailExistsAsync(email))
        {
            return EmailAlreadyRegistered(email);
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            // The raw password is hashed here and never persisted or logged.
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = Enum.Parse<UserRole>(request.Role),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await _userRepository.InsertAsync(user);
        }
        catch (DuplicateEmailException)
        {
            // Two registrations for the same email arrived at once; the unique
            // index rejected the loser.
            return EmailAlreadyRegistered(email);
        }

        _logger.LogInformation("Registered user {UserId} with role {Role}.", user.Id, user.Role);

        return StatusCode(StatusCodes.Status201Created, ToUserResponse(user));
    }

    /// <summary>
    /// Exchanges credentials for a signed access token. Allowed roles: anonymous.
    /// </summary>
    /// <response code="200">Credentials accepted; a signed JWT is returned.</response>
    /// <response code="400">Email or password was missing.</response>
    /// <response code="401">The credentials were not valid.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = NormaliseEmail(request.Email);
        var user = await _userRepository.GetByEmailAsync(email);

        if (user is null)
        {
            // Hash anyway, so an unknown email costs the same as a known one and
            // response time does not reveal which addresses are registered.
            _ = _passwordHasher.Hash(request.Password);
            return InvalidCredentials(email, "no account for this email");
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return InvalidCredentials(email, "password mismatch");
        }

        if (!user.IsActive)
        {
            return InvalidCredentials(email, "account is deactivated");
        }

        var token = _jwtTokenService.CreateAccessToken(user);

        _logger.LogInformation("Issued access token for user {UserId}.", user.Id);

        return Ok(new AuthResponse
        {
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            User = ToUserResponse(user)
        });
    }

    /// <summary>
    /// Starts a password reset: emails a time-limited link to the address given.
    /// Allowed roles: anonymous.
    /// </summary>
    /// <remarks>
    /// Always answers <c>202</c>, whether or not the address has an account and
    /// whether or not the email got through. Anything else would turn this into
    /// a way to find out who is registered — and it is reachable by anyone, with
    /// no credential needed.
    /// </remarks>
    /// <response code="202">The request was accepted. A link was sent if the address has an active account.</response>
    /// <response code="400">The email address was missing or malformed.</response>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormaliseEmail(request.Email);
        var user = await _userRepository.GetByEmailAsync(email);
        var now = DateTime.UtcNow;

        if (user is null || !user.IsActive)
        {
            // A deactivated account is treated as no account: letting it back in
            // through a reset link would undo the deactivation.
            _logger.LogInformation(
                "Password reset requested for {Email}: no active account, nothing sent.", email);

            return ResetRequestAccepted();
        }

        // Only the newest link works. Asking again because the first email went
        // astray should not leave two ways into the account outstanding.
        var invalidated = await _passwordResetTokenRepository
            .InvalidateOutstandingForUserAsync(user.Id, now);

        var minted = _passwordResetTokenService.Mint(now);

        await _passwordResetTokenRepository.InsertAsync(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            // The hash, never the token: what goes in the email cannot be read
            // back out of the database.
            TokenHash = minted.TokenHash,
            ExpiresAtUtc = minted.ExpiresAtUtc,
            CreatedAtUtc = now
        });

        try
        {
            await _passwordResetNotifier.SendResetLinkAsync(user, minted, cancellationToken);

            _logger.LogInformation(
                "Password reset link issued for user {UserId}, expiring at {ExpiresAtUtc:o}. {Invalidated} earlier link(s) invalidated.",
                user.Id, minted.ExpiresAtUtc, invalidated);
        }
        catch (Exception ex)
        {
            // Logged loudly, but still answered with the same 202. A failure
            // reported to the caller could only ever happen for an address that
            // does have an account, which is exactly what must not be leaked.
            // The unused token simply expires.
            _logger.LogError(ex, "Could not send the password reset email for user {UserId}.", user.Id);
        }

        return ResetRequestAccepted();
    }

    /// <summary>
    /// Completes a password reset: redeems the emailed token and replaces the
    /// password. Allowed roles: anonymous.
    /// </summary>
    /// <remarks>
    /// Redeeming is what retires the old password — the stored hash is
    /// overwritten, so nothing can verify against it afterwards. The link is
    /// spent in the same breath and cannot be used again.
    /// </remarks>
    /// <response code="200">The password was changed. The old one no longer works.</response>
    /// <response code="400">The payload failed validation, or the link was unknown, expired or already used.</response>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var tokenHash = _passwordResetTokenService.HashToken(request.Token);
        var resetToken = await _passwordResetTokenRepository.GetByTokenHashAsync(tokenHash);
        var now = DateTime.UtcNow;

        if (resetToken is null)
        {
            return InvalidResetLink("no reset link matches this token");
        }

        // One clock reading decides both halves of this, so a link cannot be
        // read as live here and expired a few statements later.
        if (!resetToken.IsRedeemableAt(now))
        {
            return InvalidResetLink(resetToken.ConsumedAtUtc is not null
                ? "the link has already been used"
                : $"the link expired at {resetToken.ExpiresAtUtc:o}");
        }

        var user = await _userRepository.GetByIdAsync(resetToken.UserId);

        if (user is null || !user.IsActive)
        {
            return InvalidResetLink("the account is gone or deactivated");
        }

        // Spend the link before changing anything. Two requests carrying the
        // same link can both get this far; only the one that wins this UPDATE
        // goes on to set a password.
        if (!await _passwordResetTokenRepository.MarkConsumedAsync(resetToken.Id, now))
        {
            return InvalidResetLink("the link was used by a request that arrived first");
        }

        var newPasswordHash = _passwordHasher.Hash(request.NewPassword);

        if (!await _userRepository.UpdatePasswordHashAsync(user.Id, newPasswordHash, now))
        {
            // The account was deleted between the read above and this write.
            return InvalidResetLink("the account no longer exists");
        }

        // Belt and braces: completing a reset leaves nothing outstanding, even
        // if a link was somehow issued between the request and this moment.
        await _passwordResetTokenRepository.InvalidateOutstandingForUserAsync(user.Id, now);

        _logger.LogInformation(
            "Password reset completed for user {UserId}. The previous password no longer works.", user.Id);

        return Ok(new MessageResponse
        {
            Message = "Your password has been reset. Sign in with your new password."
        });
    }

    /// <summary>
    /// Emails are stored and compared in a single canonical form, so
    /// "Ada@Example.com" cannot be registered alongside "ada@example.com".
    /// </summary>
    private static string NormaliseEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static UserResponse ToUserResponse(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        ContactAddress = user.ContactAddress,
        Role = user.Role.ToString()
    };

    private IActionResult EmailAlreadyRegistered(string email)
    {
        _logger.LogInformation("Registration rejected: {Email} is already registered.", email);

        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Email already registered",
            Detail = "An account with this email address already exists."
        });
    }

    /// <summary>
    /// The one answer <c>forgot-password</c> ever gives.
    /// </summary>
    private IActionResult ResetRequestAccepted() =>
        Accepted(new MessageResponse { Message = ResetRequestedMessage });

    /// <summary>
    /// Refuses a reset link. As with a failed login, the real reason goes to the
    /// log and the caller sees one message covering all of them.
    /// </summary>
    private IActionResult InvalidResetLink(string reason)
    {
        _logger.LogWarning("Password reset refused: {Reason}.", reason);

        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid reset link",
            Detail = InvalidResetLinkMessage
        });
    }

    /// <summary>
    /// The real reason is written to the log for operators; the caller only ever
    /// sees one generic message.
    /// </summary>
    private IActionResult InvalidCredentials(string email, string reason)
    {
        _logger.LogWarning("Login failed for {Email}: {Reason}.", email, reason);

        return Unauthorized(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Invalid credentials",
            Detail = InvalidCredentialsMessage
        });
    }
}
