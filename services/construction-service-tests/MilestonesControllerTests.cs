using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="MilestonesController"/> over a stand-in repository: how each
/// return shape from the repository maps to an HTTP result, and that the
/// name reaches the repository trimmed. The role gating is pinned by
/// <see cref="EndpointRoleDeclarationTests"/>; the real gate check and
/// aggregate query by the database-backed suite.
/// </summary>
public class MilestonesControllerTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid MilestoneId = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly DateTime Now = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    private static Milestone SampleMilestone(MilestoneStatus status = MilestoneStatus.NotStarted) => new()
    {
        Id = MilestoneId,
        ProjectId = ProjectId,
        Name = "Foundation poured",
        Status = status,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    // ---------- Create ----------

    [Fact]
    public async Task Create_returns_201_with_the_milestone_and_points_Location_at_the_list()
    {
        var repository = new FakeMilestoneRepository { NextCreatedMilestone = SampleMilestone() };
        var controller = new MilestonesController(repository);

        var result = await controller.Create(
            ProjectId,
            new CreateMilestoneRequest { Name = "Foundation poured" },
            default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(MilestonesController.List), created.ActionName);
        Assert.Equal(ProjectId, created.RouteValues!["projectId"]);

        var body = Assert.IsType<MilestoneResponse>(created.Value);
        Assert.Equal(MilestoneId, body.Id);
        Assert.Equal(ProjectId, body.ProjectId);
        Assert.Equal("Foundation poured", body.Name);
        Assert.Equal(MilestoneStatus.NotStarted, body.Status);
    }

    [Fact]
    public async Task Create_trims_the_name_before_it_reaches_the_repository()
    {
        // Trailing spaces from a copy-paste should not become part of the
        // stored name — a caller who types "Foundation" and one who pastes
        // "Foundation " must land on the same unique-key slot.
        var repository = new FakeMilestoneRepository { NextCreatedMilestone = SampleMilestone() };
        var controller = new MilestonesController(repository);

        await controller.Create(
            ProjectId,
            new CreateMilestoneRequest { Name = "  Foundation poured  " },
            default);

        var call = Assert.Single(repository.CreateCalls);
        Assert.Equal("Foundation poured", call.Name);
    }

    [Fact]
    public async Task Create_returns_400_when_the_design_has_not_yet_been_approved()
    {
        // The AC-1 gate: a null return from CreateAsync means milestone_setups
        // has no row for this project — the DesignApproved event has not been
        // consumed for it. The 400 detail names that specific reason.
        var repository = new FakeMilestoneRepository { NextCreatedMilestone = null };
        var controller = new MilestonesController(repository);

        var result = await controller.Create(
            ProjectId,
            new CreateMilestoneRequest { Name = "Foundation poured" },
            default);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        var problem = Assert.IsAssignableFrom<ProblemDetails>(objectResult.Value);
        Assert.Contains("design", problem.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approved", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_returns_409_when_the_name_is_already_taken_for_this_project()
    {
        // uq_construction_milestones_project_name refused a repeat, the
        // repository translated the MySQL error to a typed exception, and
        // the controller maps that to 409 with the milestone named.
        var repository = new FakeMilestoneRepository
        {
            CreateThrows = new DuplicateMilestoneNameException(ProjectId, "Foundation poured")
        };
        var controller = new MilestonesController(repository);

        var result = await controller.Create(
            ProjectId,
            new CreateMilestoneRequest { Name = "Foundation poured" },
            default);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var problem = Assert.IsAssignableFrom<ProblemDetails>(objectResult.Value);
        Assert.Contains("Foundation poured", problem.Detail!);
        // The project id used to leak into the message ("...for project
        // 00000000-0000-0000-0000-000000000000."). The PM is already on the
        // project's own page and the Guid reads as machine noise — pinned
        // out so a future edit that puts it back is caught.
        Assert.DoesNotContain(ProjectId.ToString(), problem.Detail!);
    }

    // ---------- List ----------

    [Fact]
    public async Task List_returns_a_row_per_milestone_mapped_from_the_repository()
    {
        var older = SampleMilestone(MilestoneStatus.Completed);
        var newer = new Milestone
        {
            Id = Guid.Parse("11111111-0000-4000-8000-000000000002"),
            ProjectId = ProjectId,
            Name = "Roof on",
            Status = MilestoneStatus.InProgress,
            CreatedAtUtc = Now.AddDays(1),
            UpdatedAtUtc = Now.AddDays(1)
        };

        // ListForProjectAsync hands rows back oldest-first (the order the PM
        // planned them in); the controller passes that order through.
        var repository = new FakeMilestoneRepository { ListRows = [older, newer] };
        var controller = new MilestonesController(repository);

        var result = Assert.IsType<OkObjectResult>(await controller.List(ProjectId, default));
        var rows = Assert.IsAssignableFrom<IEnumerable<MilestoneResponse>>(result.Value).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(older.Id, rows[0].Id);
        Assert.Equal(MilestoneStatus.Completed, rows[0].Status);
        Assert.Equal(newer.Id, rows[1].Id);
        Assert.Equal("Roof on", rows[1].Name);
        Assert.Equal(MilestoneStatus.InProgress, rows[1].Status);
    }

    [Fact]
    public async Task List_returns_an_empty_list_when_no_milestones_have_been_defined()
    {
        // Deliberately not gated on milestone_setups: a project with no
        // milestones yet is a real state, whether the design was just
        // approved or nobody has defined any. The caller who wants the
        // distinction reads /progress.
        var controller = new MilestonesController(new FakeMilestoneRepository());

        var result = Assert.IsType<OkObjectResult>(await controller.List(ProjectId, default));

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<MilestoneResponse>>(result.Value));
    }

    // ---------- UpdateStatus ----------

    [Fact]
    public async Task UpdateStatus_returns_200_with_the_milestone_as_it_now_stands()
    {
        var repository = new FakeMilestoneRepository
        {
            NextUpdatedMilestone = SampleMilestone(MilestoneStatus.InProgress)
        };
        var controller = new MilestonesController(repository);

        var result = await controller.UpdateStatus(
            MilestoneId,
            new UpdateMilestoneStatusRequest { Status = MilestoneStatus.InProgress },
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<MilestoneResponse>(ok.Value);
        Assert.Equal(MilestoneStatus.InProgress, body.Status);

        var call = Assert.Single(repository.UpdateStatusCalls);
        Assert.Equal(MilestoneId, call.MilestoneId);
        Assert.Equal(MilestoneStatus.InProgress, call.NewStatus);
    }

    [Fact]
    public async Task UpdateStatus_returns_404_when_no_milestone_has_that_id()
    {
        var repository = new FakeMilestoneRepository { NextUpdatedMilestone = null };
        var controller = new MilestonesController(repository);

        var result = await controller.UpdateStatus(
            MilestoneId,
            new UpdateMilestoneStatusRequest { Status = MilestoneStatus.Completed },
            default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- GetProgress ----------

    [Fact]
    public async Task GetProgress_returns_200_with_the_derived_rollup()
    {
        // AC-3: the percentage is derived — the controller returns exactly
        // what the repository's aggregate query computed, unchanged.
        var repository = new FakeMilestoneRepository
        {
            NextProgress = new ProjectProgress
            {
                ProjectId = ProjectId,
                TotalMilestones = 4,
                CompletedMilestones = 1,
                ProgressPercent = 25.00m
            }
        };
        var controller = new MilestonesController(repository);

        var result = await controller.GetProgress(ProjectId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ProjectProgressResponse>(ok.Value);
        Assert.Equal(ProjectId, body.ProjectId);
        Assert.Equal(4, body.TotalMilestones);
        Assert.Equal(1, body.CompletedMilestones);
        Assert.Equal(25.00m, body.ProgressPercent);
    }

    [Fact]
    public async Task GetProgress_returns_404_when_the_design_has_not_yet_been_approved()
    {
        // No milestone_setups row for the project — reporting 0% would
        // falsely suggest a plan exists.
        var repository = new FakeMilestoneRepository { NextProgress = null };
        var controller = new MilestonesController(repository);

        var result = await controller.GetProgress(ProjectId, default);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        var problem = Assert.IsAssignableFrom<ProblemDetails>(objectResult.Value);
        Assert.Contains("design", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- CreateFromTemplate ----------

    [Fact]
    public async Task CreateFromTemplate_returns_201_with_the_full_template_set()
    {
        var repository = new FakeMilestoneRepository
        {
            NextTemplateResult = MilestoneTemplates.Canonical
                .Select((name, index) => new Milestone
                {
                    Id = Guid.Parse($"22222222-0000-4000-8000-{index:D12}"),
                    ProjectId = ProjectId,
                    Name = name,
                    Status = MilestoneStatus.NotStarted,
                    CreatedAtUtc = Now.AddTicks(index),
                    UpdatedAtUtc = Now.AddTicks(index),
                })
                .ToList()
        };
        var controller = new MilestonesController(repository);

        var result = await controller.CreateFromTemplate(ProjectId, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(MilestonesController.List), created.ActionName);
        Assert.Equal(ProjectId, created.RouteValues!["projectId"]);

        var rows = Assert.IsAssignableFrom<IEnumerable<MilestoneResponse>>(created.Value).ToList();
        Assert.Equal(MilestoneTemplates.Canonical, rows.Select(r => r.Name).ToList());
        Assert.All(rows, r => Assert.Equal(MilestoneStatus.NotStarted, r.Status));

        // The controller passed the canonical template names through
        // unchanged — no mutation, no reordering.
        var call = Assert.Single(repository.CreateFromTemplateCalls);
        Assert.Equal(ProjectId, call.ProjectId);
        Assert.Equal(MilestoneTemplates.Canonical, call.TemplateNames);
    }

    [Fact]
    public async Task CreateFromTemplate_returns_400_when_the_design_has_not_been_approved()
    {
        // Same gate as Create — null return from the repo becomes a 400
        // with the same reason the PM sees on a single-milestone create.
        var repository = new FakeMilestoneRepository { NextTemplateResult = null };
        var controller = new MilestonesController(repository);

        var result = await controller.CreateFromTemplate(ProjectId, default);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        var problem = Assert.IsAssignableFrom<ProblemDetails>(objectResult.Value);
        Assert.Contains("design", problem.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approved", problem.Detail!, StringComparison.OrdinalIgnoreCase);
    }
}