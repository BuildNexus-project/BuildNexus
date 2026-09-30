using System.Security.Claims;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="ConstructionPhaseController"/> over a stand-in repository: how each
/// transition outcome maps to an HTTP answer (US-14 AC-3). The role gating is
/// pinned by <see cref="EndpointRoleDeclarationTests"/>; the preconditions
/// themselves by <see cref="ConstructionPhaseRepositoryDatabaseTests"/>.
/// </summary>
/// <remarks>
/// AC-3 is the reason most of this file is refusals rather than happy paths: three
/// transitions each with their own preconditions means the interesting behaviour is
/// what happens when one is not met, and each rejection has to arrive with a reason
/// the Project Manager can act on rather than a generic failure.
/// </remarks>
public class ConstructionPhaseControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    /// <summary>The signed-in Project Manager making the transitions.</summary>
    private static readonly Guid CallerId = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly DateTime StartedAt = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = new(2026, 9, 14, 16, 45, 0, DateTimeKind.Utc);
    private static readonly DateTime HandedOverAt = new(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc);

    // ---------- Start ----------

    [Fact]
    public async Task Start_returns_200_with_the_newly_started_phase()
    {
        var repository = new FakeConstructionPhaseRepository
        {
            NextStartResult = ConstructionTransitionResult.Succeeded(StartedPhase())
        };
        var controller = Controller(repository);

        var result = await controller.Start(ProjectId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ConstructionPhaseResponse>(ok.Value);

        Assert.Equal(ProjectId, body.ProjectId);
        Assert.Equal(ConstructionPhaseStatus.Started, body.Status);
        Assert.Equal(StartedAt, body.StartedAtUtc);
        // A build that has only just started has been through neither of the later
        // stages, and the nulls are what the PM's screen reads to know that.
        Assert.Null(body.CompletedAtUtc);
        Assert.Null(body.HandedOverAtUtc);

        var call = Assert.Single(repository.StartCalls);
        Assert.Equal(ProjectId, call.ProjectId);
        // The acting PM reaches the repository, so the event can name who decided.
        Assert.Equal(CallerId, call.ActingUserId);
    }

    [Theory]
    [InlineData(ConstructionTransitionOutcome.DesignNotApproved)]
    [InlineData(ConstructionTransitionOutcome.NoMilestonesDefined)]
    [InlineData(ConstructionTransitionOutcome.AlreadyStarted)]
    public async Task Start_returns_409_naming_the_precondition_that_refused_it(
        ConstructionTransitionOutcome outcome)
    {
        // Each of AC-1's two preconditions, plus the repeat-start case, refused on
        // its own terms — the whole point of AC-3 is that these are distinguishable
        // rather than one undifferentiated failure.
        var repository = new FakeConstructionPhaseRepository
        {
            NextStartResult = ConstructionTransitionResult.Rejected(outcome)
        };
        var controller = Controller(repository);

        var result = await controller.Start(ProjectId, default);

        var problem = AssertConflict(result);
        // The machine-readable half, so the frontend need not match on prose.
        Assert.Equal(outcome.ToString(), problem.Extensions["reason"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task Start_explains_a_missing_milestone_plan_differently_from_an_unapproved_design()
    {
        // Two 409s with the same status code must not read the same: the PM's next
        // action is "define a milestone" in one case and "get the design approved"
        // in the other.
        var noMilestones = await RefusedStartProblem(ConstructionTransitionOutcome.NoMilestonesDefined);
        var notApproved = await RefusedStartProblem(ConstructionTransitionOutcome.DesignNotApproved);

        Assert.NotEqual(noMilestones.Detail, notApproved.Detail);
        Assert.NotEqual(noMilestones.Title, notApproved.Title);
        Assert.Contains("milestone", noMilestones.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("design", notApproved.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_start_answers_with_no_phase_body()
    {
        // Nothing was written, so there is no phase to report. Answering with one
        // would tell the caller a transition happened that did not.
        var repository = new FakeConstructionPhaseRepository
        {
            NextStartResult = ConstructionTransitionResult.Rejected(
                ConstructionTransitionOutcome.NoMilestonesDefined)
        };
        var controller = Controller(repository);

        var result = await controller.Start(ProjectId, default);

        Assert.IsNotType<ConstructionPhaseResponse>(Assert.IsAssignableFrom<ObjectResult>(result).Value);
    }

    // ---------- Complete ----------

    [Fact]
    public async Task Complete_returns_200_with_the_completed_phase()
    {
        var repository = new FakeConstructionPhaseRepository
        {
            NextCompleteResult = ConstructionTransitionResult.Succeeded(CompletedPhase())
        };
        var controller = Controller(repository);

        var result = await controller.Complete(ProjectId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ConstructionPhaseResponse>(ok.Value);

        Assert.Equal(ConstructionPhaseStatus.Completed, body.Status);
        // started_at is carried through rather than restamped, so the screen can
        // still show when the build began.
        Assert.Equal(StartedAt, body.StartedAtUtc);
        Assert.Equal(CompletedAt, body.CompletedAtUtc);
        // Complete is not terminal — handover still follows.
        Assert.Null(body.HandedOverAtUtc);

        var call = Assert.Single(repository.CompleteCalls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(CallerId, call.ActingUserId);
    }

    [Theory]
    [InlineData(ConstructionTransitionOutcome.NotStarted)]
    [InlineData(ConstructionTransitionOutcome.MilestonesIncomplete)]
    [InlineData(ConstructionTransitionOutcome.AlreadyCompleted)]
    public async Task Complete_returns_409_naming_the_precondition_that_refused_it(
        ConstructionTransitionOutcome outcome)
    {
        var repository = new FakeConstructionPhaseRepository
        {
            NextCompleteResult = ConstructionTransitionResult.Rejected(outcome)
        };
        var controller = Controller(repository);

        var result = await controller.Complete(ProjectId, default);

        var problem = AssertConflict(result);
        Assert.Equal(outcome.ToString(), problem.Extensions["reason"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task Completing_a_build_that_never_started_is_refused_out_of_order()
    {
        // AC-3's named scenario, end to end through the controller: the transition
        // is attempted anyway and the system rejects it rather than quietly
        // inventing a phase to complete.
        var repository = new FakeConstructionPhaseRepository
        {
            NextCompleteResult = ConstructionTransitionResult.Rejected(
                ConstructionTransitionOutcome.NotStarted)
        };
        var controller = Controller(repository);

        var problem = AssertConflict(await controller.Complete(ProjectId, default));

        Assert.Equal("Construction has not started.", problem.Title);
        Assert.Contains("Start construction", problem.Detail!, StringComparison.Ordinal);
    }

    // ---------- HandOver ----------

    [Fact]
    public async Task HandOver_returns_200_with_the_terminal_phase()
    {
        var repository = new FakeConstructionPhaseRepository
        {
            NextHandOverResult = ConstructionTransitionResult.Succeeded(HandedOverPhase())
        };
        var controller = Controller(repository);

        var result = await controller.HandOver(ProjectId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ConstructionPhaseResponse>(ok.Value);

        Assert.Equal(ConstructionPhaseStatus.HandedOver, body.Status);
        Assert.Equal(HandedOverAt, body.HandedOverAtUtc);
        // Handover raises no event, so the response is where the actor surfaces.
        Assert.Equal(CallerId, body.HandedOverByUserId);
        // The whole span is still reported — the Client's summary needs all three dates.
        Assert.Equal(StartedAt, body.StartedAtUtc);
        Assert.Equal(CompletedAt, body.CompletedAtUtc);

        var call = Assert.Single(repository.HandOverCalls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(CallerId, call.ActingUserId);
    }

    [Theory]
    [InlineData(ConstructionTransitionOutcome.NotStarted)]
    [InlineData(ConstructionTransitionOutcome.NotCompleted)]
    [InlineData(ConstructionTransitionOutcome.FinalPaymentNotSettled)]
    [InlineData(ConstructionTransitionOutcome.AlreadyHandedOver)]
    public async Task HandOver_returns_409_naming_the_precondition_that_refused_it(
        ConstructionTransitionOutcome outcome)
    {
        var repository = new FakeConstructionPhaseRepository
        {
            NextHandOverResult = ConstructionTransitionResult.Rejected(outcome)
        };
        var controller = Controller(repository);

        var problem = AssertConflict(await controller.HandOver(ProjectId, default));

        Assert.Equal(outcome.ToString(), problem.Extensions["reason"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task HandOver_explains_an_unsettled_payment_in_its_own_words()
    {
        // The refusal a PM is most likely to hit and least likely to guess the cause of,
        // so its sentence has to name the payment rather than the build.
        var repository = new FakeConstructionPhaseRepository
        {
            NextHandOverResult = ConstructionTransitionResult.Rejected(
                ConstructionTransitionOutcome.FinalPaymentNotSettled)
        };

        var problem = AssertConflict(await Controller(repository).HandOver(ProjectId, default));

        Assert.Equal("Final payment not settled.", problem.Title);
        Assert.Contains("final payment", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandOver_distinguishes_an_unfinished_build_from_an_unpaid_one()
    {
        // Two 409s that must not read the same: one means "go and finish the build", the
        // other "go and chase the invoice".
        var notComplete = AssertConflict(await Controller(new FakeConstructionPhaseRepository
        {
            NextHandOverResult = ConstructionTransitionResult.Rejected(
                ConstructionTransitionOutcome.NotCompleted)
        }).HandOver(ProjectId, default));

        var notPaid = AssertConflict(await Controller(new FakeConstructionPhaseRepository
        {
            NextHandOverResult = ConstructionTransitionResult.Rejected(
                ConstructionTransitionOutcome.FinalPaymentNotSettled)
        }).HandOver(ProjectId, default));

        Assert.NotEqual(notComplete.Title, notPaid.Title);
        Assert.NotEqual(notComplete.Detail, notPaid.Detail);
    }

    [Fact]
    public async Task HandOver_refuses_a_token_with_no_usable_subject()
    {
        // The terminal move especially must not be recorded against nobody: handover
        // raises no event, so this row is the only place the author is ever written.
        var repository = new FakeConstructionPhaseRepository
        {
            NextHandOverResult = ConstructionTransitionResult.Succeeded(HandedOverPhase())
        };

        var result = await ControllerWithoutSubject(repository).HandOver(ProjectId, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(repository.HandOverCalls);
    }

    // ---------- Get ----------

    [Fact]
    public async Task Get_returns_200_with_the_phase()
    {
        var repository = new FakeConstructionPhaseRepository { NextPhase = CompletedPhase() };
        var controller = Controller(repository);

        var ok = Assert.IsType<OkObjectResult>(await controller.Get(ProjectId, default));
        var body = Assert.IsType<ConstructionPhaseResponse>(ok.Value);

        Assert.Equal(ConstructionPhaseStatus.Completed, body.Status);
        Assert.Equal(ProjectId, Assert.Single(repository.GetCalls));
    }

    [Fact]
    public async Task Get_returns_404_when_construction_has_not_started()
    {
        // The normal answer for a project that has not begun its build, not an
        // error — the PM's screen reads it as "Start construction is next".
        var repository = new FakeConstructionPhaseRepository { NextPhase = null };
        var controller = Controller(repository);

        var result = await controller.Get(ProjectId, default);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Construction has not started.", problem.Title);
    }

    [Fact]
    public async Task Start_refuses_a_token_with_no_usable_subject()
    {
        // The role claim would get them past [Authorize], but a transition with no
        // author would leave the Project Service unable to say who moved the
        // project — so it is refused rather than attributed to nobody.
        var repository = new FakeConstructionPhaseRepository
        {
            NextStartResult = ConstructionTransitionResult.Succeeded(StartedPhase())
        };

        var result = await ControllerWithoutSubject(repository).Start(ProjectId, default);

        Assert.IsType<UnauthorizedResult>(result);
        // And nothing was attempted.
        Assert.Empty(repository.StartCalls);
    }

    [Fact]
    public async Task Complete_refuses_a_token_with_no_usable_subject()
    {
        var repository = new FakeConstructionPhaseRepository
        {
            NextCompleteResult = ConstructionTransitionResult.Succeeded(CompletedPhase())
        };

        var result = await ControllerWithoutSubject(repository).Complete(ProjectId, default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(repository.CompleteCalls);
    }

    // ---------- helpers ----------

    private static async Task<ProblemDetails> RefusedStartProblem(ConstructionTransitionOutcome outcome)
    {
        var controller = Controller(new FakeConstructionPhaseRepository
        {
            NextStartResult = ConstructionTransitionResult.Rejected(outcome)
        });

        return AssertConflict(await controller.Start(ProjectId, default));
    }

    /// <summary>
    /// The controller with a signed-in Project Manager on it — the claims a real
    /// token carries, spelled the same way
    /// <see cref="ConstructionProgressControllerTests"/> spells them.
    /// </summary>
    private static ConstructionPhaseController Controller(FakeConstructionPhaseRepository repository)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.Role, "ProjectManager"),
            new(JwtRegisteredClaimNames.Sub, CallerId.ToString())
        ];

        return new ConstructionPhaseController(repository)
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

    /// <summary>The controller with a token that carried no usable subject claim.</summary>
    private static ConstructionPhaseController ControllerWithoutSubject(
        FakeConstructionPhaseRepository repository) =>
        new(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimTypes.Role, "ProjectManager")], "TestAuth"))
                }
            }
        };

    private static ProblemDetails AssertConflict(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        return problem;
    }

    private static ConstructionPhase StartedPhase() => new()
    {
        ProjectId = ProjectId,
        Status = ConstructionPhaseStatus.Started,
        StartedAtUtc = StartedAt,
        UpdatedAtUtc = StartedAt
    };

    private static ConstructionPhase HandedOverPhase() => new()
    {
        ProjectId = ProjectId,
        Status = ConstructionPhaseStatus.HandedOver,
        StartedAtUtc = StartedAt,
        CompletedAtUtc = CompletedAt,
        HandedOverAtUtc = HandedOverAt,
        HandedOverByUserId = CallerId,
        UpdatedAtUtc = HandedOverAt
    };

    private static ConstructionPhase CompletedPhase() => new()
    {
        ProjectId = ProjectId,
        Status = ConstructionPhaseStatus.Completed,
        StartedAtUtc = StartedAt,
        CompletedAtUtc = CompletedAt,
        UpdatedAtUtc = CompletedAt
    };
}
