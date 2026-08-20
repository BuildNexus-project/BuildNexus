using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using BuildNexus.UserService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.UserService.Controllers;

/// <summary>
/// Registration and login.
/// </summary>
/// <remarks>
/// Both endpoints are open to anonymous callers by design — they are how a
/// caller obtains a token in the first place. Every other endpoint in BuildNexus
/// requires a bearer token and names the roles allowed to call it.
/// </remarks>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    /// <summary>Returned for every login failure, whatever the underlying cause.</summary>
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
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
            Role = Enum.Parse<UserRole>(request.Role, ignoreCase: true),
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
