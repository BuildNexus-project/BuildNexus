using System.Security.Claims;
using BuildNexus.ConstructionService.Authorization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Controllers;

/// <summary>
/// The Project Manager's formal transitions through the build phase: starting
/// construction and marking it complete (US-14).
/// </summary>
/// <remarks>
/// Project-Manager only, on every endpoint — the story is framed around that role,
/// and these are the decisions that move a project through its build. A separate
/// controller from <see cref="MilestonesController"/> even though both are
/// PM-only: that one is about planning and tracking the work inside the build,
/// this one is about the build's own lifecycle, and each transition here reads
/// milestone state rather than changing it.
/// <para>
/// Every precondition lives in <see cref="IConstructionPhaseRepository"/>, checked
/// inside the transaction that writes — see its remarks for why. This controller's
/// whole job is to turn the outcome into the right HTTP answer.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Roles = PlatformRoles.ProjectManager)]
public class ConstructionPhaseController : ControllerBase
{
    private readonly IConstructionPhaseRepository _phases;

    public ConstructionPhaseController(IConstructionPhaseRepository phases)
    {
        _phases = phases;
    }

    /// <summary>
    /// Formally starts construction on a project whose design is approved and whose
    /// milestones are defined (AC-1).
    /// </summary>
    /// <remarks>
    /// On success a <c>ConstructionStarted</c> event is enqueued in the same
    /// transaction and reaches <c>construction-events</c> shortly afterwards — the
    /// Project Service's cue to move the project to <c>Construction</c>. A refused
    /// transition enqueues nothing.
    /// </remarks>
    /// <response code="200">The phase as it now stands, newly Started.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    /// <response code="409">A precondition refused the transition: the design is not approved, no milestones are defined, or construction has already started.</response>
    [HttpPost("api/construction/projects/{projectId:guid}/start")]
    [ProducesResponseType(typeof(ConstructionPhaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var startedBy))
        {
            // The role claim got them this far, but a transition with no author
            // would leave the Project Service's audit trail unable to say who
            // moved the project.
            return Unauthorized();
        }

        var result = await _phases.StartAsync(projectId, startedBy, cancellationToken);

        return result.Outcome is ConstructionTransitionOutcome.Succeeded
            ? Ok(ConstructionPhaseResponse.From(result.Phase!))
            : Refused(result.Outcome);
    }

    /// <summary>
    /// Marks construction complete once every milestone on the project is
    /// <c>Completed</c> (AC-2).
    /// </summary>
    /// <remarks>
    /// Gated independently of <see cref="Start"/>: construction must already be
    /// under way <em>and</em> every milestone done. On success a
    /// <c>ConstructionCompleted</c> event is enqueued in the same transaction.
    /// </remarks>
    /// <response code="200">The phase as it now stands, newly Completed.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid, or carried no usable subject claim.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    /// <response code="409">A precondition refused the transition: construction has not started, a milestone is unfinished, or it is already complete.</response>
    [HttpPost("api/construction/projects/{projectId:guid}/complete")]
    [ProducesResponseType(typeof(ConstructionPhaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid projectId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var completedBy))
        {
            return Unauthorized();
        }

        var result = await _phases.CompleteAsync(projectId, completedBy, cancellationToken);

        return result.Outcome is ConstructionTransitionOutcome.Succeeded
            ? Ok(ConstructionPhaseResponse.From(result.Phase!))
            : Refused(result.Outcome);
    }

    /// <summary>
    /// Where the project's build phase stands, so the PM's screen can show only the
    /// transition that is actually available next.
    /// </summary>
    /// <remarks>
    /// A 404 is the normal answer for a project whose build has not started — the
    /// absence of a phase is a real state, not a failure, and the screen reads it as
    /// "Start construction is the next step". So it is not treated as an error by
    /// the caller.
    /// </remarks>
    /// <response code="200">The phase.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not a Project Manager.</response>
    /// <response code="404">Construction has not been started on this project.</response>
    [HttpGet("api/construction/projects/{projectId:guid}/phase")]
    [ProducesResponseType(typeof(ConstructionPhaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var phase = await _phases.GetForProjectAsync(projectId, cancellationToken);

        if (phase is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Construction has not started.",
                detail: "This project's build has not been started yet.");
        }

        return Ok(ConstructionPhaseResponse.From(phase));
    }

    /// <summary>
    /// The caller's own id, from the token's <c>sub</c> claim — spelled the same way
    /// <see cref="ConstructionProgressController"/> reads it.
    /// </summary>
    private bool TryGetCallerId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    /// <summary>
    /// Turns a refused transition into its HTTP answer (AC-3).
    /// </summary>
    /// <remarks>
    /// Always a 409, whichever precondition refused. Every one of these endpoints is
    /// a state transition and every refusal here means the same thing — the project
    /// is not in a state where this move is legal — so one status code keeps the
    /// frontend to a single branch. The request itself is never malformed: there is
    /// no body, and the project id was validated by the route constraint before the
    /// action ran, which is what would have made a 400 the right answer.
    /// <para>
    /// The specific reason travels two ways. <c>detail</c> is the sentence the PM
    /// reads — the frontend renders it verbatim — and the <c>reason</c> extension
    /// carries the outcome's own name, so a caller that needs to branch on which
    /// precondition failed can do it without matching on English prose.
    /// </para>
    /// </remarks>
    private IActionResult Refused(ConstructionTransitionOutcome outcome)
    {
        var (title, detail) = Describe(outcome);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail
        };

        problem.Extensions["reason"] = outcome.ToString();

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }

    /// <summary>
    /// The sentence each refusal shows the Project Manager.
    /// </summary>
    /// <remarks>
    /// Written to say what to do next rather than only what went wrong: "define at
    /// least one milestone first" is actionable where "precondition failed" is not.
    /// No project id in any of them — the PM is looking at the project's own page,
    /// where that is context they already have, and a raw Guid in the sentence reads
    /// as machine noise. Same reasoning as
    /// <see cref="DuplicateMilestoneNameException"/>'s message.
    /// </remarks>
    private static (string Title, string Detail) Describe(ConstructionTransitionOutcome outcome) =>
        outcome switch
        {
            ConstructionTransitionOutcome.DesignNotApproved => (
                "Design not yet approved.",
                "Construction can only be started once this project's design has been approved."),

            ConstructionTransitionOutcome.NoMilestonesDefined => (
                "No milestones defined.",
                "Define at least one construction milestone before starting the build."),

            ConstructionTransitionOutcome.AlreadyStarted => (
                "Construction already started.",
                "This project's build has already been started."),

            ConstructionTransitionOutcome.NotStarted => (
                "Construction has not started.",
                "Start construction before marking it complete."),

            ConstructionTransitionOutcome.MilestonesIncomplete => (
                "Milestones are not all complete.",
                "Every milestone must be marked Completed before construction can be completed."),

            ConstructionTransitionOutcome.AlreadyCompleted => (
                "Construction already complete.",
                "This project's build has already been marked complete."),

            // Succeeded never reaches here, and a member added to the enum without a
            // sentence should fail loudly rather than answer with an empty problem
            // that says nothing to the PM and nothing to the log.
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome), outcome, "No problem-details message is defined for this outcome.")
        };
}
