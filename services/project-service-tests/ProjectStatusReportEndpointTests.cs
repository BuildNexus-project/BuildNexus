using System.Text;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Controllers;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The US-18 acceptance criteria walked over a fake report query: projects come
/// back grouped by status, the filters reach the query, a bad filter is refused,
/// and the export carries the same report.
/// </summary>
/// <remarks>
/// Who may call these is not decided in the action but by its role attribute, so
/// that is pinned in <c>EndpointRoleDeclarationTests</c>; here the caller is
/// simply the Admin the route lets through.
/// </remarks>
public class ProjectStatusReportEndpointTests
{
    private static readonly DateTime Created = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    // ----------------------------------------------------- AC-1: grouped ----

    [Fact]
    public async Task Groups_every_project_by_its_status()
    {
        var (controller, _) = ControllerWith(
            Row(ProjectStatus.Pending),
            Row(ProjectStatus.Pending),
            Row(ProjectStatus.Designing),
            Row(ProjectStatus.Construction),
            Row(ProjectStatus.Completed));

        var report = await Report(controller.GetStatusReport(null, null, null, default));

        Assert.Equal(5, report.TotalProjects);
        Assert.Equal(2, GroupOf(report, "Pending").Count);
        Assert.Equal(1, GroupOf(report, "Designing").Count);
        Assert.Equal(1, GroupOf(report, "Construction").Count);
        Assert.Equal(1, GroupOf(report, "Completed").Count);
        Assert.All(report.Groups, group =>
            Assert.All(group.Projects, project => Assert.Equal(group.Status, project.Status)));
    }

    [Fact]
    public async Task Reports_every_status_in_lifecycle_order_even_those_with_no_projects()
    {
        var (controller, _) = ControllerWith(Row(ProjectStatus.Construction));

        var report = await Report(controller.GetStatusReport(null, null, null, default));

        Assert.Equal(
            ["Pending", "Designing", "DesignApproved", "Construction", "Completed", "Cancelled"],
            report.Groups.Select(group => group.Status));
        Assert.Equal(0, GroupOf(report, "Designing").Count);
    }

    [Fact]
    public async Task Carries_each_projects_details_and_the_budget_totals()
    {
        var id = Guid.NewGuid();
        var (controller, _) = ControllerWith(
            Row(ProjectStatus.Pending, id: id, name: "Beachfront villa", budget: 18_500_000m),
            Row(ProjectStatus.Pending, budget: 1_500_000m));

        var report = await Report(controller.GetStatusReport(null, null, null, default));

        var pending = GroupOf(report, "Pending");
        var villa = Assert.Single(pending.Projects, project => project.Id == id);
        Assert.Equal("Beachfront villa", villa.Name);
        Assert.Equal("Galle", villa.Location);
        Assert.Equal(18_500_000m, villa.Budget);
        Assert.Equal(Created, villa.CreatedAt);
        Assert.Equal(20_000_000m, pending.TotalBudget);
        Assert.Equal(20_000_000m, report.TotalBudget);
    }

    [Fact]
    public async Task An_empty_pipeline_is_a_report_of_empty_groups()
    {
        var (controller, _) = ControllerWith();

        var report = await Report(controller.GetStatusReport(null, null, null, default));

        Assert.Equal(0, report.TotalProjects);
        Assert.Equal(6, report.Groups.Count);
    }

    [Fact]
    public async Task Says_when_the_report_was_generated()
    {
        var (controller, _) = ControllerWith();
        var before = DateTime.UtcNow;

        var report = await Report(controller.GetStatusReport(null, null, null, default));

        Assert.InRange(report.GeneratedAt, before, DateTime.UtcNow);
    }

    // ------------------------------------------------- AC-2: filterable ----

    [Fact]
    public async Task With_no_filters_asks_for_the_whole_pipeline()
    {
        var (controller, repository) = ControllerWith();

        await controller.GetStatusReport(null, null, null, default);

        Assert.Null(repository.LastFilter!.Statuses);
        Assert.Null(repository.LastFilter.From);
        Assert.Null(repository.LastFilter.To);
    }

    [Fact]
    public async Task Passes_the_status_filter_to_the_query_and_reports_only_those_statuses()
    {
        var (controller, repository) = ControllerWith(Row(ProjectStatus.Designing), Row(ProjectStatus.Completed));

        var report = await Report(controller.GetStatusReport(["Designing", "Completed"], null, null, default));

        Assert.Equal([ProjectStatus.Designing, ProjectStatus.Completed], repository.LastFilter!.Statuses);
        Assert.Equal(["Designing", "Completed"], report.Groups.Select(group => group.Status));
    }

    [Fact]
    public async Task Passes_the_date_range_to_the_query()
    {
        var (controller, repository) = ControllerWith();

        await controller.GetStatusReport(null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        Assert.Equal(new DateOnly(2026, 9, 1), repository.LastFilter!.From);
        Assert.Equal(new DateOnly(2026, 9, 30), repository.LastFilter.To);
    }

    [Fact]
    public async Task Status_and_date_range_combine()
    {
        var (controller, repository) = ControllerWith();

        await controller.GetStatusReport(["Pending"], new DateOnly(2026, 9, 1), null, default);

        Assert.Equal([ProjectStatus.Pending], repository.LastFilter!.Statuses);
        Assert.Equal(new DateOnly(2026, 9, 1), repository.LastFilter.From);
        Assert.Null(repository.LastFilter.To);
    }

    [Fact]
    public async Task An_unknown_status_is_refused_with_400_and_the_query_never_runs()
    {
        var (controller, repository) = ControllerWith(Row(ProjectStatus.Pending));

        var result = await controller.GetStatusReport(["Finished"], null, null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repository.Queries);
    }

    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_refused_with_400()
    {
        var (controller, repository) = ControllerWith();

        var result = await controller.GetStatusReport(null, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1), default);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repository.Queries);
    }

    [Fact]
    public async Task The_refusal_says_what_was_wrong()
    {
        var (controller, _) = ControllerWith();

        var result = await controller.GetStatusReport(["Finished"], null, null, default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);
        Assert.Contains("Finished", Assert.Single(problem.Errors["filter"]));
    }

    // --------------------------------------------------- AC-2: exportable ----

    [Fact]
    public async Task The_export_is_a_csv_file_named_for_the_day()
    {
        var (controller, _) = ControllerWith(Row(ProjectStatus.Pending));

        var file = Assert.IsType<FileContentResult>(await controller.ExportStatusReport(null, null, null, default));

        Assert.StartsWith("text/csv", file.ContentType);
        Assert.Equal($"project-status-report-{DateTime.UtcNow:yyyy-MM-dd}.csv", file.FileDownloadName);
    }

    [Fact]
    public async Task The_export_starts_with_a_byte_order_mark_and_holds_the_same_projects()
    {
        var id = Guid.NewGuid();
        var (controller, _) = ControllerWith(
            Row(ProjectStatus.Pending, id: id, name: "Beachfront villa"),
            Row(ProjectStatus.Completed, name: "Hilltop cottage"));

        var file = Assert.IsType<FileContentResult>(await controller.ExportStatusReport(null, null, null, default));

        Assert.Equal(Encoding.UTF8.GetPreamble(), file.FileContents.Take(3).ToArray());

        var csv = Encoding.UTF8.GetString(file.FileContents, 3, file.FileContents.Length - 3);
        Assert.Contains($"Pending,{id},Beachfront villa,", csv);
        Assert.Contains("Completed,", csv);
        Assert.Contains("Hilltop cottage", csv);
    }

    [Fact]
    public async Task The_export_honours_the_same_filters()
    {
        var (controller, repository) = ControllerWith(Row(ProjectStatus.Designing), Row(ProjectStatus.Pending));

        var file = Assert.IsType<FileContentResult>(
            await controller.ExportStatusReport(["Designing"], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default));

        Assert.Equal([ProjectStatus.Designing], repository.LastFilter!.Statuses);
        Assert.Equal(new DateOnly(2026, 9, 1), repository.LastFilter.From);
        Assert.Equal(new DateOnly(2026, 9, 30), repository.LastFilter.To);

        // The fake hands back both rows, but only the Designing group is in
        // scope, so only that one is written.
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Designing,", csv);
        Assert.DoesNotContain("Pending,", csv);
    }

    [Fact]
    public async Task A_bad_filter_refuses_the_export_too()
    {
        var (controller, repository) = ControllerWith();

        var result = await controller.ExportStatusReport(["Finished"], null, null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(0, repository.Queries);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ProjectStatusReportResponse> Report(Task<IActionResult> action) =>
        Assert.IsType<ProjectStatusReportResponse>(Assert.IsType<OkObjectResult>(await action).Value);

    private static ProjectStatusGroupResponse GroupOf(ProjectStatusReportResponse report, string status) =>
        Assert.Single(report.Groups, group => group.Status == status);

    private static int? StatusOf(IActionResult result) => Assert.IsAssignableFrom<ObjectResult>(result).StatusCode;

    private static (ReportsController Controller, FakeProjectReportRepository Repository) ControllerWith(
        params ProjectReportRow[] rows)
    {
        var repository = new FakeProjectReportRepository();
        repository.Seed(rows);

        var controller = new ReportsController(repository)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return (controller, repository);
    }

    private static ProjectReportRow Row(
        ProjectStatus status,
        Guid? id = null,
        string name = "Test project",
        decimal budget = 1_000_000m) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Location = "Galle",
        Status = status,
        Budget = budget,
        CreatedAt = Created,
        UpdatedAt = Created
    };
}
