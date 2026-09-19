using BuildNexus.ConstructionService.Authorization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ConstructionService.Controllers;

/// <summary>
/// The construction milestones a Project Manager defines for an approved
/// project, and the derived project-level progress they roll up to (US-12).
/// </summary>
/// <remarks>
/// Project-Manager only, on every endpoint — the story is framed around that
/// role and no other role has business creating, moving, or reading these
/// rows for this story. Broader read access is a later-story concern; the
/// controller's role gate is the single place to widen it if that changes.
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.ProjectManager)]
public class MilestonesController : ControllerBase
{
    private readonly IMilestoneRepository _repository;

    public MilestonesController(IMilestoneRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Adds a milestone to a project — only if the project has already had
    /// its design approved (AC-1).
    /// </summary>
    /// <response code="201">The milestone as stored.</response>
    /// <response code="400">The name was missing or too long, or the project's design has not yet been approved.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    /// <response code="409">A milestone with that name already exists for the project.</response>
    [HttpPost("api/construction/projects/{projectId:guid}/milestones")]
    [ProducesResponseType(typeof(MilestoneResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid projectId,
        [FromBody] CreateMilestoneRequest request,
        CancellationToken cancellationToken)
    {
        // ModelState covers [Required] and [StringLength] — a blank or
        // oversize name is refused before the repository is touched.
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var name = request.Name.Trim();

        try
        {
            var milestone = await _repository.CreateAsync(projectId, name, cancellationToken);

            if (milestone is null)
            {
                // AC-1: the milestone_setups row is not there — the
                // DesignApproved event has not been consumed for this project,
                // so its design is not yet approved. A 400 with the specific
                // reason beats a generic one; the frontend's ApiError picks up
                // the detail as-is.
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Design not yet approved.",
                    detail: "Milestones can only be created for a project once its design has been approved.");
            }

            var response = MilestoneResponse.From(milestone);

            // CreatedAtAction points at the list, not a per-milestone GET —
            // the story does not need one, and inventing an endpoint just to
            // fill Location would exceed scope. The listing is the natural
            // place to see the row that was just created.
            return CreatedAtAction(nameof(List), new { projectId }, response);
        }
        catch (DuplicateMilestoneNameException ex)
        {
            // The (project_id, name) unique index refused a repeat. A 409 is
            // the standard answer for a state-based conflict, and its detail
            // names the milestone so the PM knows which one clashed.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Milestone name already used.",
                detail: ex.Message);
        }
    }

    /// <summary>
    /// Every milestone defined for a project, oldest first — the order the PM
    /// planned them in.
    /// </summary>
    /// <response code="200">The milestones, oldest first. Empty when nothing has been defined yet.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    [HttpGet("api/construction/projects/{projectId:guid}/milestones")]
    [ProducesResponseType(typeof(IReadOnlyList<MilestoneResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var milestones = await _repository.ListForProjectAsync(projectId, cancellationToken);

        return Ok(milestones.Select(MilestoneResponse.From).ToList());
    }

    /// <summary>
    /// Moves a milestone to a new status (AC-2). The three-state domain is
    /// enforced by the model binder — anything outside <c>NotStarted</c>,
    /// <c>InProgress</c> or <c>Completed</c> is refused as a 400 before this
    /// method runs.
    /// </summary>
    /// <response code="200">The milestone as it now stands.</response>
    /// <response code="400">The status was missing or not one of the three allowed values.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    /// <response code="404">No milestone has that id.</response>
    [HttpPatch("api/construction/milestones/{id:guid}/status")]
    [ProducesResponseType(typeof(MilestoneResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateMilestoneStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var milestone = await _repository.UpdateStatusAsync(id, request.Status, cancellationToken);

        if (milestone is null)
        {
            return NotFound();
        }

        return Ok(MilestoneResponse.From(milestone));
    }

    /// <summary>
    /// The project's aggregated milestone progress (AC-3), recomputed on
    /// every read.
    /// </summary>
    /// <remarks>
    /// The percentage is not stored anywhere — this endpoint answers from a
    /// single aggregate query, so a caller can never see a stored total that
    /// disagrees with the milestones behind it.
    /// </remarks>
    /// <response code="200">The progress rollup.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    /// <response code="404">The project's design has not yet been approved, so it has no progress to report.</response>
    [HttpGet("api/construction/projects/{projectId:guid}/progress")]
    [ProducesResponseType(typeof(ProjectProgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgress(Guid projectId, CancellationToken cancellationToken)
    {
        var progress = await _repository.GetProgressForProjectAsync(projectId, cancellationToken);

        if (progress is null)
        {
            // No milestone_setups row — the project's design has not been
            // approved. A 404 with a reason beats a bare one; a "0%" answer
            // for a plan that does not exist would mislead.
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Progress not available.",
                detail: "This project's design has not yet been approved, so it has no milestone progress to report.");
        }

        return Ok(ProjectProgressResponse.From(progress));
    }
}