using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// What <see cref="MilestonesController"/> asks of the milestone repository,
/// checked with a Moq mock (US-14).
/// </summary>
/// <remarks>
/// The older <see cref="MilestonesControllerTests"/> use a hand-written fake. These
/// stub what the repository answers and verify what the controller sent it: the
/// trimmed name, the status exactly as requested, and — on every failure path —
/// that nothing was written.
/// </remarks>
public class MilestonesControllerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid MilestoneId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly DateOnly DueDate = new(2026, 9, 14);

    private readonly Mock<IMilestoneRepository> _milestones = new();

    public static TheoryData<DateOnly?> DueDates => new()
    {
        null,
        DueDate,
        DateOnly.MinValue,
        DateOnly.MaxValue
    };

    // ------------------------------------------------------------- creating ----

    [Theory]
    [InlineData("Foundations", "Foundations")]
    [InlineData("  Foundations", "Foundations")]
    [InlineData("Foundations  ", "Foundations")]
    [InlineData("  Roof and gutters  ", "Roof and gutters")]
    public async Task A_milestone_is_created_once_with_its_name_trimmed(string typed, string expectedStored)
    {
        _milestones
            .Setup(m => m.CreateAsync(ProjectId, expectedStored, DueDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MilestoneNamed(expectedStored));

        var result = await new MilestonesController(_milestones.Object).Create(
            ProjectId, new CreateMilestoneRequest { Name = typed, DueDate = DueDate }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(expectedStored, Assert.IsType<MilestoneResponse>(created.Value).Name);
        _milestones.Verify(
            m => m.CreateAsync(ProjectId, expectedStored, DueDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_milestone_for_a_project_whose_design_is_not_approved_is_a_400()
    {
        // The repository answers null when there is no milestone setup yet.
        _milestones
            .Setup(m => m.CreateAsync(
                ProjectId, It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Milestone?)null);

        var result = await new MilestonesController(_milestones.Object).Create(
            ProjectId, new CreateMilestoneRequest { Name = "Foundations" }, default);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("Design not yet approved.", Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    [Fact]
    public async Task A_milestone_name_already_used_on_the_project_is_a_409_naming_it()
    {
        _milestones
            .Setup(m => m.CreateAsync(
                ProjectId, "Foundations", It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateMilestoneNameException(ProjectId, "Foundations"));

        var result = await new MilestonesController(_milestones.Object).Create(
            ProjectId, new CreateMilestoneRequest { Name = "Foundations" }, default);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("Foundations", Assert.IsType<ProblemDetails>(problem.Value).Detail);
    }

    [Fact]
    public async Task An_invalid_milestone_is_refused_before_the_repository_is_touched()
    {
        var controller = new MilestonesController(_milestones.Object);
        controller.ModelState.AddModelError(nameof(CreateMilestoneRequest.Name), "A milestone name is required.");

        var result = await controller.Create(ProjectId, new CreateMilestoneRequest { Name = "" }, default);

        Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);
        _milestones.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_template_creates_the_canonical_milestones_once_for_the_route_project()
    {
        _milestones
            .Setup(m => m.CreateFromTemplateAsync(
                ProjectId, MilestoneTemplates.Canonical, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MilestoneTemplates.Canonical.Select(MilestoneNamed).ToList());

        var result = await new MilestonesController(_milestones.Object).CreateFromTemplate(ProjectId, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsAssignableFrom<IReadOnlyList<MilestoneResponse>>(created.Value);
        Assert.Equal(MilestoneTemplates.Canonical.Count, body.Count);
        _milestones.Verify(
            m => m.CreateFromTemplateAsync(ProjectId, MilestoneTemplates.Canonical, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_template_for_a_project_whose_design_is_not_approved_is_a_400()
    {
        _milestones
            .Setup(m => m.CreateFromTemplateAsync(
                ProjectId, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Milestone>?)null);

        var result = await new MilestonesController(_milestones.Object).CreateFromTemplate(ProjectId, default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    // ------------------------------------------------------- changing status ----

    [Theory]
    [InlineData(MilestoneStatus.NotStarted)]
    [InlineData(MilestoneStatus.InProgress)]
    [InlineData(MilestoneStatus.Completed)]
    public async Task Each_status_reaches_the_repository_exactly_as_requested(MilestoneStatus status)
    {
        _milestones
            .Setup(m => m.UpdateStatusAsync(MilestoneId, status, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MilestoneNamed("Foundations", status));

        var result = await new MilestonesController(_milestones.Object).UpdateStatus(
            MilestoneId, new UpdateMilestoneStatusRequest { Status = status }, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(status, Assert.IsType<MilestoneResponse>(ok.Value).Status);
        _milestones.Verify(
            m => m.UpdateStatusAsync(MilestoneId, status, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_status_change_on_an_unknown_milestone_is_a_404()
    {
        _milestones
            .Setup(m => m.UpdateStatusAsync(MilestoneId, It.IsAny<MilestoneStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Milestone?)null);

        var result = await new MilestonesController(_milestones.Object).UpdateStatus(
            MilestoneId, new UpdateMilestoneStatusRequest { Status = MilestoneStatus.Completed }, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task A_status_change_with_no_status_is_refused_and_nothing_is_demoted()
    {
        // Without [Required] on the nullable status, an empty body would bind to
        // NotStarted and quietly demote a Completed milestone.
        var controller = new MilestonesController(_milestones.Object);
        controller.ModelState.AddModelError(nameof(UpdateMilestoneStatusRequest.Status), "A status is required.");

        var result = await controller.UpdateStatus(MilestoneId, new UpdateMilestoneStatusRequest(), default);

        Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);
        _milestones.Verify(
            m => m.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<MilestoneStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------ due dates ----

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task A_due_date_reaches_the_repository_as_given_and_null_clears_it(DateOnly? dueDate)
    {
        _milestones
            .Setup(m => m.SetDueDateAsync(MilestoneId, dueDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MilestoneNamed("Foundations"));

        var result = await new MilestonesController(_milestones.Object).SetDueDate(
            MilestoneId, new SetMilestoneDueDateRequest { DueDate = dueDate }, default);

        Assert.IsType<OkObjectResult>(result);
        _milestones.Verify(
            m => m.SetDueDateAsync(MilestoneId, dueDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_due_date_on_an_unknown_milestone_is_a_404()
    {
        _milestones
            .Setup(m => m.SetDueDateAsync(MilestoneId, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Milestone?)null);

        var result = await new MilestonesController(_milestones.Object).SetDueDate(
            MilestoneId, new SetMilestoneDueDateRequest { DueDate = DueDate }, default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ------------------------------------------------------------- progress ----

    [Fact]
    public async Task Progress_for_a_project_with_no_milestone_plan_is_a_404_not_zero_percent()
    {
        _milestones
            .Setup(m => m.GetProgressForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectProgress?)null);

        var result = await new MilestonesController(_milestones.Object).GetProgress(ProjectId, default);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Progress_is_reported_as_the_repository_computed_it()
    {
        _milestones
            .Setup(m => m.GetProgressForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectProgress
            {
                ProjectId = ProjectId,
                TotalMilestones = 4,
                CompletedMilestones = 1,
                ProgressPercent = 25m
            });

        var result = await new MilestonesController(_milestones.Object).GetProgress(ProjectId, default);

        var body = Assert.IsType<ProjectProgressResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(25m, body.ProgressPercent);
        Assert.Equal(1, body.CompletedMilestones);
    }

    private static Milestone MilestoneNamed(string name) => MilestoneNamed(name, MilestoneStatus.NotStarted);

    private static Milestone MilestoneNamed(string name, MilestoneStatus status) => new()
    {
        Id = MilestoneId,
        ProjectId = ProjectId,
        Name = name,
        Status = status,
        DueDate = DueDate,
        CreatedAtUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc)
    };
}
