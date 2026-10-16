using System.Security.Claims;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// What <see cref="ConstructionPhaseController"/> asks of the phase repository for
/// each transition, checked with a Moq mock (US-14).
/// </summary>
/// <remarks>
/// The older <see cref="ConstructionPhaseControllerTests"/> use a hand-written
/// fake. These stub the repository's answer and then verify the call: a refused
/// transition (start twice, complete before starting, hand over unpaid) must come
/// back as a 409 naming the reason, and exactly one repository call must have been
/// made — never one for a caller whose token has no subject.
/// </remarks>
public class ConstructionPhaseControllerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid CallerId = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly DateTime StartedAt = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IConstructionPhaseRepository> _phases = new();

    /// <summary>The three transitions a Project Manager can ask for.</summary>
    public enum Step
    {
        Start,
        Complete,
        HandOver
    }

    [Theory]
    [InlineData(Step.Start, ConstructionPhaseStatus.Started)]
    [InlineData(Step.Complete, ConstructionPhaseStatus.Completed)]
    [InlineData(Step.HandOver, ConstructionPhaseStatus.HandedOver)]
    public async Task A_transition_that_succeeds_is_a_200_and_asks_the_repository_once_for_the_caller(
        Step step,
        ConstructionPhaseStatus expectedStatus)
    {
        Stub(step, ConstructionTransitionResult.Succeeded(PhaseWith(expectedStatus)));

        var result = await Run(step, Controller(CallerId));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expectedStatus, Assert.IsType<ConstructionPhaseResponse>(ok.Value).Status);

        VerifyAskedOnce(step);
        _phases.VerifyNoOtherCalls();
    }

    // ---------------------------------------- transitions that are not allowed ----

    [Theory]
    [InlineData(Step.Start, ConstructionTransitionOutcome.DesignNotApproved)]
    [InlineData(Step.Start, ConstructionTransitionOutcome.NoMilestonesDefined)]
    [InlineData(Step.Start, ConstructionTransitionOutcome.AlreadyStarted)]
    [InlineData(Step.Complete, ConstructionTransitionOutcome.NotStarted)]
    [InlineData(Step.Complete, ConstructionTransitionOutcome.MilestonesIncomplete)]
    [InlineData(Step.Complete, ConstructionTransitionOutcome.AlreadyCompleted)]
    [InlineData(Step.HandOver, ConstructionTransitionOutcome.NotStarted)]
    [InlineData(Step.HandOver, ConstructionTransitionOutcome.NotCompleted)]
    [InlineData(Step.HandOver, ConstructionTransitionOutcome.FinalPaymentNotSettled)]
    [InlineData(Step.HandOver, ConstructionTransitionOutcome.AlreadyHandedOver)]
    public async Task A_transition_the_project_is_not_ready_for_is_a_409_naming_the_reason(
        Step step,
        ConstructionTransitionOutcome outcome)
    {
        Stub(step, ConstructionTransitionResult.Rejected(outcome));

        var result = await Run(step, Controller(CallerId));

        var conflict = Assert.IsAssignableFrom<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);

        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(outcome.ToString(), problem.Extensions["reason"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));

        // One attempt, and not a different transition tried in its place.
        VerifyAskedOnce(step);
        _phases.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_outcome_with_no_message_defined_fails_loudly_instead_of_answering_with_an_empty_problem()
    {
        // A value added to the enum without a sentence for the PM. Not a real
        // outcome today — 999 stands in for "the next one someone forgets".
        Stub(Step.Start, ConstructionTransitionResult.Rejected((ConstructionTransitionOutcome)999));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Run(Step.Start, Controller(CallerId)));
    }

    // -------------------------------------------------------- no usable caller ----

    [Theory]
    [InlineData(Step.Start)]
    [InlineData(Step.Complete)]
    [InlineData(Step.HandOver)]
    public async Task A_token_without_a_usable_subject_is_unauthorized_and_the_repository_is_never_asked(Step step)
    {
        // A transition with no author would leave the audit trail unable to say
        // who moved the project.
        var result = await Run(step, Controller(callerId: null));

        Assert.IsType<UnauthorizedResult>(result);
        _phases.VerifyNoOtherCalls();
    }

    // ----------------------------------------------------------------- reading ----

    [Fact]
    public async Task A_project_whose_build_has_not_started_has_no_phase_to_read_and_is_a_404()
    {
        _phases
            .Setup(p => p.GetForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConstructionPhase?)null);

        var result = await Controller(CallerId).Get(ProjectId, default);

        var notFound = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        _phases.Verify(p => p.GetForProjectAsync(ProjectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------- helpers ----

    private void Stub(Step step, ConstructionTransitionResult answer)
    {
        switch (step)
        {
            case Step.Start:
                _phases
                    .Setup(p => p.StartAsync(ProjectId, CallerId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(answer);
                break;
            case Step.Complete:
                _phases
                    .Setup(p => p.CompleteAsync(ProjectId, CallerId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(answer);
                break;
            default:
                _phases
                    .Setup(p => p.HandOverAsync(ProjectId, CallerId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(answer);
                break;
        }
    }

    private void VerifyAskedOnce(Step step)
    {
        switch (step)
        {
            case Step.Start:
                _phases.Verify(p => p.StartAsync(ProjectId, CallerId, It.IsAny<CancellationToken>()), Times.Once);
                break;
            case Step.Complete:
                _phases.Verify(p => p.CompleteAsync(ProjectId, CallerId, It.IsAny<CancellationToken>()), Times.Once);
                break;
            default:
                _phases.Verify(p => p.HandOverAsync(ProjectId, CallerId, It.IsAny<CancellationToken>()), Times.Once);
                break;
        }
    }

    private static Task<IActionResult> Run(Step step, ConstructionPhaseController controller) =>
        step switch
        {
            Step.Start => controller.Start(ProjectId, default),
            Step.Complete => controller.Complete(ProjectId, default),
            _ => controller.HandOver(ProjectId, default)
        };

    private ConstructionPhaseController Controller(Guid? callerId)
    {
        List<Claim> claims = [new(ClaimTypes.Role, "ProjectManager")];

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ConstructionPhaseController(_phases.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            }
        };
    }

    private static ConstructionPhase PhaseWith(ConstructionPhaseStatus status) => new()
    {
        ProjectId = ProjectId,
        Status = status,
        StartedAtUtc = StartedAt,
        UpdatedAtUtc = StartedAt
    };
}
