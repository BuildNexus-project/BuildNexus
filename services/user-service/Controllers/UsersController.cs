using System.Security.Claims;
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
/// action runs.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public UsersController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// Returns the profile of the caller.
    /// Allowed roles: Client, Architect, ProjectManager, Admin.
    /// </summary>
    /// <response code="200">The caller's profile.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="404">The token is valid but the account no longer exists.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(subject, out var userId))
        {
            // A token that passed signature validation but carries no usable
            // subject is not something we can act on.
            return Unauthorized();
        }

        var user = await _userRepository.GetByIdAsync(userId);

        return user is null ? NotFound() : Ok(ToUserResponse(user));
    }

    /// <summary>
    /// Returns any user's profile by id. Allowed roles: Admin.
    /// </summary>
    /// <response code="200">The requested profile.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is authenticated but is not an Admin.</response>
    /// <response code="404">No user with this id.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);

        return user is null ? NotFound() : Ok(ToUserResponse(user));
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
