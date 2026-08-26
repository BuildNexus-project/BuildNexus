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
