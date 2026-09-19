using BuildNexus.DesignService.Contracts;
using BuildNexus.DesignService.Controllers;
using BuildNexus.DesignService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The design approval report endpoint over a stand-in repository: what it
/// returns and how a row is mapped. The Admin-only gating is pinned by
/// <see cref="EndpointRoleDeclarationTests"/>; the aggregate SQL by
/// <see cref="DesignReportRepositoryDatabaseTests"/>.
/// </summary>
public class ReportsControllerTests
{
    [Fact]
    public async Task GetApprovalReport_returns_a_row_per_project_mapped_from_the_repository()
    {
        var approved = new DesignApprovalReportRow
        {
            ProjectId = Guid.Parse("11111111-0000-4000-8000-000000000001"),
            DocumentCount = 2,
            ApprovedDocumentCount = 2,
            TotalVersionCount = 5,
            AverageVersionsToApproval = 2.5,
            AverageHoursToApproval = 6.0
        };

        var inProgress = new DesignApprovalReportRow
        {
            ProjectId = Guid.Parse("22222222-0000-4000-8000-000000000002"),
            DocumentCount = 1,
            ApprovedDocumentCount = 0,
            TotalVersionCount = 4,
            AverageVersionsToApproval = null,
            AverageHoursToApproval = null
        };

        var repository = new FakeDesignReportRepository { Rows = [approved, inProgress] };
        var controller = new ReportsController(repository);

        var result = Assert.IsType<OkObjectResult>(await controller.GetApprovalReport(default));
        var rows = Assert.IsAssignableFrom<IEnumerable<DesignApprovalReportRowResponse>>(result.Value).ToList();

        Assert.Equal(2, rows.Count);

        Assert.Equal(approved.ProjectId, rows[0].ProjectId);
        Assert.Equal(2, rows[0].ApprovedDocumentCount);
        Assert.Equal(5, rows[0].TotalVersionCount);
        Assert.Equal(2.5, rows[0].AverageVersionsToApproval);
        Assert.Equal(6.0, rows[0].AverageHoursToApproval);

        // The in-progress project is still a row, with nulls where nothing is approved.
        Assert.Equal(inProgress.ProjectId, rows[1].ProjectId);
        Assert.Equal(4, rows[1].TotalVersionCount);
        Assert.Null(rows[1].AverageVersionsToApproval);
        Assert.Null(rows[1].AverageHoursToApproval);
    }

    [Fact]
    public async Task GetApprovalReport_returns_an_empty_list_when_no_project_has_design_documents()
    {
        var controller = new ReportsController(new FakeDesignReportRepository());

        var result = Assert.IsType<OkObjectResult>(await controller.GetApprovalReport(default));

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<DesignApprovalReportRowResponse>>(result.Value));
    }
}
