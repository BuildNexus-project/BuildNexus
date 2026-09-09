using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.UserService.Controllers;

/// <summary>
/// Endpoints for other BuildNexus services, not for a browser — see
/// <see cref="InternalServiceAuthenticationHandler"/> for how a caller proves
/// it belongs here.
/// </summary>
[ApiController]
[Route("api/internal")]
[Authorize(AuthenticationSchemes = InternalServiceAuthenticationHandler.SchemeName)]
public class InternalController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public InternalController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// Looks up one user's name and email, for a service that needs to notify
    /// or display them — e.g. the Design Service naming the Architect on a
    /// revision request. Allowed callers: any BuildNexus service holding the
    /// shared internal key.
    /// </summary>
    /// <response code="200">The user's name and email.</response>
    /// <response code="401">The internal API key was missing or wrong.</response>
    /// <response code="404">No user with this id.</response>
    [HttpGet("users/{id:guid}")]
    [ProducesResponseType(typeof(InternalUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(new InternalUserResponse { Id = user.Id, FullName = user.FullName, Email = user.Email });
    }
}
