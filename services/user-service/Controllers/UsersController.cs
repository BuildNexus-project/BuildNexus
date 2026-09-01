using System.Security.Claims;
using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.UserService.Controllers;

/// <summary>
/// Protected user endpoints. Every action here requires a valid bearer token —
/// an expired, tampered or malformed token is rejected with 401 before the
/// action runs — and names the roles allowed to call it, so a caller holding a
/// good token for the wrong role is refused with 403.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserRepository userRepository, ILogger<UsersController> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <summary>
    /// Returns the profile of the caller.
    /// Allowed roles: Client, Architect, ProjectManager, Admin.
    /// </summary>
    /// <response code="200">The caller's profile.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The token carries no recognised platform role.</response>
    /// <response code="404">The token is valid but the account no longer exists.</response>
    [HttpGet("me")]
    [Authorize(Roles = PlatformRoles.AnyRole)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser()
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _userRepository.GetByIdAsync(userId);

        return user is null ? NotFound() : Ok(ToUserResponse(user));
    }

    /// <summary>
    /// Updates the caller's own full name and contact details.
    /// Allowed roles: Client, Architect, ProjectManager, Admin.
    /// </summary>
    /// <remarks>
    /// Email and role are not self-editable: a payload carrying either is
    /// refused with 400, and the update statement behind this action does not
    /// list those columns in the first place.
    /// </remarks>
    /// <response code="200">The saved profile, as it now stands.</response>
    /// <response code="400">The payload failed validation, or tried to change email or role.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The token carries no recognised platform role.</response>
    /// <response code="404">The token is valid but the account no longer exists.</response>
    [HttpPut("me")]
    [Authorize(Roles = PlatformRoles.AnyRole)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCurrentUser([FromBody] UpdateProfileRequest request)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        // The account is read first so the response can carry the untouched
        // email and role beside the fields that did change.
        var user = await _userRepository.GetByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = NullIfBlank(request.PhoneNumber);
        user.ContactAddress = NullIfBlank(request.ContactAddress);
        user.UpdatedAt = DateTime.UtcNow;

        if (!await _userRepository.UpdateProfileAsync(user))
        {
            // The account was removed between the read and the write.
            return NotFound();
        }

        _logger.LogInformation("Updated profile for user {UserId}.", user.Id);

        return Ok(ToUserResponse(user));
    }

    /// <summary>
    /// Lists the Architects and Project Managers available to work on a project.
    /// Allowed roles: Architect, ProjectManager.
    /// </summary>
    /// <remarks>
    /// The two roles that staff and deliver a project can look each other up.
    /// A Client is refused — a customer has no business browsing the firm's
    /// staff — and so is an Admin, who administers accounts through
    /// <c>GET /api/users</c> rather than taking part in project work.
    /// Deactivated accounts are left out: they cannot be given work.
    /// </remarks>
    /// <response code="200">The active project staff, ordered by name.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is a Client or an Admin.</response>
    [HttpGet("directory")]
    [Authorize(Roles = PlatformRoles.ProjectStaff)]
    [ProducesResponseType(typeof(IReadOnlyList<DirectoryEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProjectStaffDirectory()
    {
        var staff = await _userRepository.ListActiveByRolesAsync([UserRole.Architect, UserRole.ProjectManager]);

        return Ok(staff.Select(ToDirectoryEntry).ToList());
    }

    /// <summary>
    /// Lists the accounts on the platform, a page at a time and optionally
    /// narrowed to one role. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Deactivated accounts are included — an administrator has to be able to
    /// see them, and reinstating one starts with finding it.
    /// <para>
    /// Paged at the database rather than here: a directory that grows past a few
    /// hundred accounts should not be read in full to show twenty of them.
    /// A page past the end is a valid question with an empty answer, not a 404 —
    /// the total in the response is what tells the caller they overshot.
    /// </para>
    /// </remarks>
    /// <response code="200">The requested page, ordered by name, with the total beside it.</response>
    /// <response code="400">The page, page size or role filter was not usable.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is authenticated but is not an Admin.</response>
    [HttpGet]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(PagedResponse<UserSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] UserListQuery query)
    {
        var page = await _userRepository.ListPageAsync(query.ParsedRole(), query.Page, query.PageSize);

        return Ok(new PagedResponse<UserSummaryResponse>
        {
            Items = page.Items.Select(ToUserSummary).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = page.TotalCount
        });
    }

    /// <summary>
    /// Returns any user's profile by id. Allowed roles: Admin.
    /// </summary>
    /// <response code="200">The requested profile.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is authenticated but is not an Admin.</response>
    /// <response code="404">No user with this id.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);

        return user is null ? NotFound() : Ok(ToUserResponse(user));
    }

    /// <summary>
    /// Replaces another account's name, email and role. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// The two fields a user may not change on themselves — the email they sign
    /// in with and the role that decides what they may do — are changed here,
    /// which is what makes this the answer to the "ask an administrator" the
    /// profile form gives them.
    /// <para>
    /// An Admin may not change their own role. Only an Admin can reach this
    /// endpoint, so self-demotion is the one edit that could leave the platform
    /// with nobody able to undo it.
    /// </para>
    /// </remarks>
    /// <response code="200">The account as it now stands.</response>
    /// <response code="400">The payload failed validation, or the caller tried to change their own role.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is authenticated but is not an Admin.</response>
    /// <response code="404">No user with this id.</response>
    /// <response code="409">The email already belongs to another account.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] AdminUpdateUserRequest request)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        // Read first, so the response can carry the fields this endpoint does
        // not touch and so the role can be compared against what is stored.
        var user = await _userRepository.GetByIdAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        var role = Enum.Parse<UserRole>(request.Role, ignoreCase: true);

        if (id == callerId && role != user.Role)
        {
            return SelfInflicted(
                "cannot change your own role",
                "You cannot change your own role. Ask another administrator to do it.",
                nameof(request.Role));
        }

        // Stored and compared in the one canonical form every other endpoint
        // uses, so "Ada@Example.com" cannot be moved onto an address that
        // already exists as "ada@example.com".
        var email = NormaliseEmail(request.Email);

        // Checked ahead of the write so the usual case answers with a clear
        // conflict rather than an exception; the unique index is still the final
        // word, and the catch below is what makes that race safe.
        if (!string.Equals(email, user.Email, StringComparison.Ordinal)
            && await _userRepository.EmailExistsAsync(email))
        {
            return EmailAlreadyRegistered(email);
        }

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.Role = role;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            if (!await _userRepository.UpdateAccountAsync(user))
            {
                // The account was removed between the read and the write.
                return NotFound();
            }
        }
        catch (DuplicateEmailException)
        {
            // Two administrators moved two accounts onto the same address at
            // once; the unique index rejected the loser.
            return EmailAlreadyRegistered(email);
        }

        _logger.LogInformation(
            "Admin {AdminId} updated account {UserId}; role is now {Role}.", callerId, user.Id, user.Role);

        return Ok(ToUserSummary(user));
    }

    /// <summary>
    /// Withdraws or restores an account's access. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Deactivating is what stops the holder signing in: login refuses an
    /// inactive account, and so does a password reset, so a link cannot be used
    /// to undo this. Nothing the account has already authored is affected.
    /// <para>
    /// An Admin may not deactivate their own account. Only an Admin can reach
    /// this endpoint, so that one rule is what guarantees an active
    /// administrator always remains — the caller.
    /// </para>
    /// </remarks>
    /// <response code="200">The account as it now stands.</response>
    /// <response code="400">The flag was missing, or the caller tried to deactivate themselves.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is authenticated but is not an Admin.</response>
    /// <response code="404">No user with this id.</response>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetUserActive(Guid id, [FromBody] SetUserActiveRequest request)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        // Validation has already refused an absent flag.
        var isActive = request.IsActive!.Value;

        if (id == callerId && !isActive)
        {
            return SelfInflicted(
                "cannot deactivate your own account",
                "You cannot deactivate your own account. Ask another administrator to do it.",
                nameof(request.IsActive));
        }

        // Read first so the response carries the whole row, not just the flag
        // that moved — the directory replaces the edited row with what comes
        // back rather than re-reading the page.
        var user = await _userRepository.GetByIdAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;

        if (!await _userRepository.SetActiveAsync(user.Id, isActive, user.UpdatedAt))
        {
            // The account was removed between the read and the write.
            return NotFound();
        }

        _logger.LogInformation(
            "Admin {AdminId} {Action} account {UserId}.",
            callerId, isActive ? "reinstated" : "deactivated", user.Id);

        return Ok(ToUserSummary(user));
    }

    /// <summary>
    /// Reads the caller's id from the token's <c>sub</c> claim. A token that
    /// passed signature validation but carries no usable subject is not
    /// something we can act on.
    /// </summary>
    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    /// <summary>
    /// Blank in, <c>null</c> out — clearing a contact detail in the profile form
    /// must empty the column rather than store an empty string.
    /// </summary>
    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Emails are stored and compared in a single canonical form, matching how
    /// registration and login normalise them — otherwise an account could be
    /// moved to an address its owner could never sign in with.
    /// </summary>
    private static string NormaliseEmail(string email) =>
        email.Trim().ToLowerInvariant();

    /// <summary>
    /// Refuses an edit an administrator aimed at their own account. Returned as
    /// a field error so the offending control in the form is the one that lights
    /// up, rather than a banner above it.
    /// </summary>
    private IActionResult SelfInflicted(string reason, string message, string field)
    {
        _logger.LogWarning("Admin self-edit refused: {Reason}.", reason);

        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [field] = [message]
        }));
    }

    private IActionResult EmailAlreadyRegistered(string email)
    {
        _logger.LogInformation("Account update rejected: {Email} is already registered.", email);

        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Email already registered",
            Detail = "An account with this email address already exists."
        });
    }

    private static UserSummaryResponse ToUserSummary(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt
    };

    private static DirectoryEntryResponse ToDirectoryEntry(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Role = user.Role.ToString()
    };

    private static UserResponse ToUserResponse(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        ContactAddress = user.ContactAddress,
        Role = user.Role.ToString()
    };
}
