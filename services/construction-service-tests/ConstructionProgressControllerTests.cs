using System.Security.Claims;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="ConstructionProgressController"/> over stand-in repositories: the
/// ownership gate that keeps one Client out of another's project, and how each
/// repository answer maps to an HTTP result (US-13).
/// </summary>
/// <remarks>
/// The role gating is pinned by <see cref="EndpointRoleDeclarationTests"/>, the
/// real ownership SQL by <see cref="ProjectOwnerRepositoryDatabaseTests"/>, and
/// the aggregate query by <see cref="MilestoneRepositoryDatabaseTests"/>. What
/// is left for here is the controller's own decisions.
/// </remarks>
public class ConstructionProgressControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly DateTime Now = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeMilestoneRepository _milestones = new();
    private readonly FakeProjectOwnerRepository _owners = new();
    private readonly FakeConstructionPhaseRepository _phases = new();

    [Fact]
    public async Task Returns_the_milestones_and_the_rollup_for_the_owning_client()
    {
        // AC-1: per-milestone status and an overall percentage, in one answer.
        GiveOwnership();
        _milestones.NextProgress = Progress(total: 4, completed: 1, percent: 25m);
        _milestones.ListRows =
        [
            SampleMilestone("Foundation", MilestoneStatus.Completed),
            SampleMilestone("Walls", MilestoneStatus.InProgress),
            SampleMilestone("Roof"),
            SampleMilestone("Finishing")
        ];

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        var body = Assert.IsType<ProjectProgressSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(ProjectId, body.ProjectId);
        Assert.Equal(4, body.TotalMilestones);
        Assert.Equal(1, body.CompletedMilestones);
        Assert.Equal(25m, body.ProgressPercent);
        Assert.Equal(
            ["Foundation", "Walls", "Roof", "Finishing"],
            body.Milestones.Select(milestone => milestone.Name));
        Assert.Equal(MilestoneStatus.Completed, body.Milestones[0].Status);
        Assert.Equal(MilestoneStatus.InProgress, body.Milestones[1].Status);
        Assert.Equal(MilestoneStatus.NotStarted, body.Milestones[2].Status);
    }

    [Fact]
    public async Task An_approved_project_with_no_milestones_yet_is_an_empty_list_at_zero_percent()
    {
        // A real state, not a failure: the design was only just approved and
        // the PM has not planned the build yet. The dashboard must be able to
        // say so rather than showing an error.
        GiveOwnership();
        _milestones.NextProgress = Progress(total: 0, completed: 0, percent: 0m);
        _milestones.ListRows = [];

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        var body = Assert.IsType<ProjectProgressSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(body.Milestones);
        Assert.Equal(0, body.TotalMilestones);
        Assert.Equal(0m, body.ProgressPercent);
    }

    [Fact]
    public async Task A_client_who_does_not_own_the_project_gets_403()
    {
        // The whole point of the ownership replica: a role gate alone would let
        // any signed-in Client read any project's build progress by guessing an
        // id.
        _owners.Owners[ProjectId] = SomeoneElse;
        _milestones.NextProgress = Progress(total: 4, completed: 4, percent: 100m);

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task A_project_this_service_has_no_owner_for_gets_403()
    {
        // The "not learned who owns it yet" case — a ProjectCreated published
        // before this service started consuming project-events. It denies
        // rather than defaulting open.
        _milestones.NextProgress = Progress(total: 2, completed: 1, percent: 50m);

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_refused_caller_is_told_nothing_about_the_project()
    {
        // A 403 must not depend on whether the project exists, is approved, or
        // has milestones — otherwise the status code itself becomes an oracle a
        // Client could enumerate ids with. The repository is never reached at
        // all, so it cannot leak through timing either.
        _owners.Owners[ProjectId] = SomeoneElse;
        _milestones.NextProgress = null;

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Ownership_is_checked_before_anything_is_read()
    {
        _owners.Owners[ProjectId] = SomeoneElse;
        _milestones.NextProgress = Progress(total: 1, completed: 1, percent: 100m);
        _milestones.ListRows = [SampleMilestone("Foundation", MilestoneStatus.Completed)];

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        // Nothing about the project was read on the way to refusing.
        Assert.Empty(_milestones.CreateCalls);
    }

    [Fact]
    public async Task A_project_whose_design_is_not_approved_yet_gets_404()
    {
        // The gate the repository answers with null: no milestone_setups row,
        // so there is no construction plan. Reporting 0% would tell the Client
        // their build has started and stalled.
        GiveOwnership();
        _milestones.NextProgress = null;

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_token_with_no_usable_subject_claim_gets_401()
    {
        // The role claim got the caller past the attribute, but without a
        // subject there is no "their own project" to scope the read to.
        GiveOwnership();
        _milestones.NextProgress = Progress(total: 1, completed: 0, percent: 0m);

        var controller = Controller(callerId: null);

        var result = await controller.GetSummary(ProjectId, default);

        Assert.IsType<UnauthorizedResult>(result);
    }

    private void GiveOwnership() => _owners.Owners[ProjectId] = ClientId;

    private static ProjectProgress Progress(int total, int completed, decimal percent) => new()
    {
        ProjectId = ProjectId,
        TotalMilestones = total,
        CompletedMilestones = completed,
        ProgressPercent = percent
    };

    private static Milestone SampleMilestone(string name, MilestoneStatus status = MilestoneStatus.NotStarted) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        Name = name,
        Status = status,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    /// <summary>
    /// The controller with a signed-in Client on it. <paramref name="callerId"/>
    /// of <c>null</c> stands for a token that carried no usable <c>sub</c>.
    /// </summary>
    // -------------------------------- the build phase (US-14 AC-4) --------

    [Fact]
    public async Task The_summary_reports_a_handed_over_project_to_its_client()
    {
        // AC-4: the terminal state comes with a summary the Client can see. This is
        // where they learn their project is finished and theirs.
        GiveOwnership();
        _milestones.NextProgress = Progress(total: 2, completed: 2, percent: 100m);
        _phases.NextPhase = new ConstructionPhase
        {
            ProjectId = ProjectId,
            Status = ConstructionPhaseStatus.HandedOver,
            StartedAtUtc = Now,
            CompletedAtUtc = Now.AddMonths(6),
            HandedOverAtUtc = Now.AddMonths(7),
            HandedOverByUserId = Guid.NewGuid(),
            UpdatedAtUtc = Now.AddMonths(7)
        };

        var body = await SummaryFor(ClientId);

        Assert.NotNull(body.Phase);
        Assert.Equal(ConstructionPhaseStatus.HandedOver, body.Phase.Status);
        // The whole span, so the summary reads as a history rather than a status.
        Assert.Equal(Now, body.Phase.StartedAtUtc);
        Assert.Equal(Now.AddMonths(6), body.Phase.CompletedAtUtc);
        Assert.Equal(Now.AddMonths(7), body.Phase.HandedOverAtUtc);
    }

    [Fact]
    public async Task The_clients_summary_does_not_name_the_staff_member_who_handed_over()
    {
        // handedOverByUserId is a staff account id: of no use to a Client and not theirs
        // to see. They are told their project was handed over and when, not which
        // employee pressed the button — so the property is absent from this shape
        // entirely rather than merely left unread.
        GiveOwnership();
        _milestones.NextProgress = Progress(total: 1, completed: 1, percent: 100m);
        _phases.NextPhase = new ConstructionPhase
        {
            ProjectId = ProjectId,
            Status = ConstructionPhaseStatus.HandedOver,
            StartedAtUtc = Now,
            CompletedAtUtc = Now.AddMonths(6),
            HandedOverAtUtc = Now.AddMonths(7),
            HandedOverByUserId = Guid.NewGuid(),
            UpdatedAtUtc = Now.AddMonths(7)
        };

        var body = await SummaryFor(ClientId);

        Assert.Null(
            typeof(ConstructionPhaseSummary).GetProperty("HandedOverByUserId"));
        Assert.DoesNotContain(
            "handedOverBy",
            System.Text.Json.JsonSerializer.Serialize(body.Phase),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_build_that_has_not_started_reports_a_null_phase_rather_than_failing()
    {
        // The normal state for a project whose design was only just approved. The
        // milestones and rollup are still worth showing, so a missing phase must not
        // take the response down with it.
        GiveOwnership();
        _milestones.NextProgress = Progress(total: 0, completed: 0, percent: 0m);
        _phases.NextPhase = null;

        var body = await SummaryFor(ClientId);

        Assert.Null(body.Phase);
        Assert.Equal(0, body.TotalMilestones);
    }

    [Fact]
    public async Task The_phase_is_not_read_for_a_project_that_is_not_the_callers()
    {
        // The ownership gate runs before anything is read, so a Client guessing ids
        // learns nothing about another project's build — not even whether it started.
        _owners.Owners[ProjectId] = SomeoneElse;

        var result = await Controller(ClientId).GetSummary(ProjectId, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Empty(_phases.GetCalls);
    }

    /// <summary>The summary body for a caller, for the cases that assert on its shape.</summary>
    private async Task<ProjectProgressSummaryResponse> SummaryFor(Guid callerId)
    {
        var result = await Controller(callerId).GetSummary(ProjectId, default);

        return Assert.IsType<ProjectProgressSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }

    private ConstructionProgressController Controller(Guid? callerId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, "Client") };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ConstructionProgressController(_milestones, _owners, _phases)
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
}
