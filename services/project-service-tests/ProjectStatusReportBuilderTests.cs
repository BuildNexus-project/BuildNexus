using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// Grouping the report's rows by status (US-18 AC-1). No database — the rows are
/// handed in, so what is under test is only the grouping.
/// </summary>
public class ProjectStatusReportBuilderTests
{
    private static readonly DateTime Day = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly ProjectReportFilter NoFilter = new();

    [Fact]
    public void Groups_projects_by_status()
    {
        var rows = new[]
        {
            Row(ProjectStatus.Pending),
            Row(ProjectStatus.Construction),
            Row(ProjectStatus.Pending),
            Row(ProjectStatus.Completed)
        };

        var report = ProjectStatusReportBuilder.Build(rows, NoFilter);

        Assert.Equal(2, GroupOf(report, ProjectStatus.Pending).Count);
        Assert.Equal(1, GroupOf(report, ProjectStatus.Construction).Count);
        Assert.Equal(1, GroupOf(report, ProjectStatus.Completed).Count);
        Assert.All(report.Groups, group =>
            Assert.All(group.Projects, project => Assert.Equal(group.Status, project.Status)));
    }

    [Fact]
    public void Reports_every_status_in_lifecycle_order_even_when_empty()
    {
        var report = ProjectStatusReportBuilder.Build([Row(ProjectStatus.Construction)], NoFilter);

        Assert.Equal(Enum.GetValues<ProjectStatus>(), report.Groups.Select(group => group.Status));
        Assert.Empty(GroupOf(report, ProjectStatus.Designing).Projects);
        Assert.Equal(0, GroupOf(report, ProjectStatus.Designing).Count);
    }

    [Fact]
    public void An_empty_pipeline_is_a_report_of_empty_groups_not_an_empty_report()
    {
        var report = ProjectStatusReportBuilder.Build([], NoFilter);

        Assert.Equal(Enum.GetValues<ProjectStatus>().Length, report.Groups.Count);
        Assert.Equal(0, report.TotalProjects);
        Assert.Equal(0m, report.TotalBudget);
    }

    [Fact]
    public void A_status_filter_reports_only_the_statuses_it_named()
    {
        var filter = new ProjectReportFilter { Statuses = [ProjectStatus.Completed, ProjectStatus.Pending] };
        var rows = new[] { Row(ProjectStatus.Pending), Row(ProjectStatus.Completed), Row(ProjectStatus.Designing) };

        var report = ProjectStatusReportBuilder.Build(rows, filter);

        // Lifecycle order, not the order the filter listed them in.
        Assert.Equal([ProjectStatus.Pending, ProjectStatus.Completed], report.Groups.Select(group => group.Status));
        Assert.Equal(2, report.TotalProjects);
    }

    [Fact]
    public void A_project_outside_the_filtered_statuses_is_left_out_rather_than_regrouped()
    {
        var filter = new ProjectReportFilter { Statuses = [ProjectStatus.Pending] };

        var report = ProjectStatusReportBuilder.Build([Row(ProjectStatus.Designing)], filter);

        Assert.Equal(0, report.TotalProjects);
    }

    [Fact]
    public void An_empty_status_selection_reports_no_groups()
    {
        var filter = new ProjectReportFilter { Statuses = [] };

        var report = ProjectStatusReportBuilder.Build([Row(ProjectStatus.Pending)], filter);

        Assert.Empty(report.Groups);
        Assert.Equal(0, report.TotalProjects);
    }

    [Fact]
    public void Totals_the_budget_per_group_and_overall()
    {
        var rows = new[]
        {
            Row(ProjectStatus.Pending, budget: 1_000_000m),
            Row(ProjectStatus.Pending, budget: 2_500_000.50m),
            Row(ProjectStatus.Construction, budget: 4_000_000m)
        };

        var report = ProjectStatusReportBuilder.Build(rows, NoFilter);

        Assert.Equal(3_500_000.50m, GroupOf(report, ProjectStatus.Pending).TotalBudget);
        Assert.Equal(4_000_000m, GroupOf(report, ProjectStatus.Construction).TotalBudget);
        Assert.Equal(0m, GroupOf(report, ProjectStatus.Completed).TotalBudget);
        Assert.Equal(7_500_000.50m, report.TotalBudget);
        Assert.Equal(3, report.TotalProjects);
    }

    [Fact]
    public void Orders_each_group_newest_first_whatever_order_the_rows_arrive_in()
    {
        var oldest = Row(ProjectStatus.Pending, createdAt: Day);
        var middle = Row(ProjectStatus.Pending, createdAt: Day.AddDays(1));
        var newest = Row(ProjectStatus.Pending, createdAt: Day.AddDays(2));

        var report = ProjectStatusReportBuilder.Build([middle, oldest, newest], NoFilter);

        Assert.Equal(
            [newest.Id, middle.Id, oldest.Id],
            GroupOf(report, ProjectStatus.Pending).Projects.Select(project => project.Id));
    }

    [Fact]
    public void Breaks_a_creation_time_tie_by_id_so_the_order_is_stable()
    {
        var low = Row(ProjectStatus.Pending, id: Guid.Parse("00000000-0000-4000-8000-000000000001"));
        var high = Row(ProjectStatus.Pending, id: Guid.Parse("ffffffff-0000-4000-8000-000000000001"));

        var report = ProjectStatusReportBuilder.Build([high, low], NoFilter);

        Assert.Equal(
            [low.Id, high.Id],
            GroupOf(report, ProjectStatus.Pending).Projects.Select(project => project.Id));
    }

    private static ProjectStatusGroup GroupOf(ProjectStatusReport report, ProjectStatus status) =>
        Assert.Single(report.Groups, group => group.Status == status);

    private static ProjectReportRow Row(
        ProjectStatus status,
        decimal budget = 1_000_000m,
        DateTime? createdAt = null,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Test project",
        Location = "Galle",
        Status = status,
        Budget = budget,
        CreatedAt = createdAt ?? Day,
        UpdatedAt = createdAt ?? Day
    };
}
