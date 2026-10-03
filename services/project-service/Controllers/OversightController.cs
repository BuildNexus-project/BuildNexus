using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ProjectService.Controllers;

/// <summary>
/// The Admin's platform oversight (US-38). Every action requires a valid bearer
/// token and is Admin-only — the Clients whose projects are listed and the staff
/// delivering them do not see the whole platform.
/// </summary>
/// <remarks>
/// Its own controller for the same reason <see cref="ReportsController"/> is: it
/// reads across every project, which is the opposite of
/// <see cref="ProjectsController"/>'s per-project access rule.
/// </remarks>
[ApiController]
[Route("api/projects/oversight")]
[Authorize(Roles = PlatformRoles.Admin)]
public class OversightController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IUserNameResolver _userNames;

    public OversightController(IProjectRepository projectRepository, IUserNameResolver userNames)
    {
        _projectRepository = projectRepository;
        _userNames = userNames;
    }

    /// <summary>
    /// Every project on the platform with its status, assigned staff and
    /// last-updated date, and which of them have stalled. Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Unlike <c>GET /api/projects</c> this leaves cancelled projects in: an Admin
    /// assessing platform health wants the whole picture, and the status says
    /// which are closed out. Newest submitted first, the order every other project
    /// list uses.
    /// <para>
    /// Staff are names, not just ids, asked of the User Service on the way out and
    /// never stored. A name that could not be found out is <c>null</c> and the id
    /// is still present, so the list is never held up by a User Service outage.
    /// </para>
    /// <para>
    /// A project is flagged <c>isStalled</c> when it is still open and has not
    /// moved for <c>stalledAfterDays</c> days — see
    /// <see cref="Models.ProjectStallPolicy"/>.
    /// </para>
    /// </remarks>
    /// <response code="200">Every project, with its staff, last-updated date and stalled flag.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ProjectOversightResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOversight(CancellationToken cancellationToken)
    {
        // One instant for every row, so the flags and the count cannot disagree.
        var generatedAt = DateTime.UtcNow;

        var projects = await _projectRepository.ListAllAsync();

        var staffIds = projects
            .SelectMany(project => new[] { project.AssignedArchitectId, project.AssignedProjectManagerId })
            .OfType<Guid>();

        var names = await _userNames.ResolveNamesAsync(staffIds, cancellationToken);

        return Ok(ProjectOversightResponse.From(projects, names, generatedAt));
    }
}
