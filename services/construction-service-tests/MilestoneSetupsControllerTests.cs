using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Controllers;
using BuildNexus.ConstructionService.Models;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="MilestoneSetupsController"/> over a stand-in repository: what it
/// returns and how a row is mapped. The Admin-only gating is pinned by
/// <see cref="EndpointRoleDeclarationTests"/>; the idempotent SQL by
/// <see cref="MilestoneSetupRepositoryDatabaseTests"/>.
/// </summary>
public class MilestoneSetupsControllerTests
{
    [Fact]
    public async Task List_returns_a_row_per_placeholder_mapped_from_the_repository()
    {
        var newer = new MilestoneSetup
        {
            Id = Guid.Parse("11111111-0000-4000-8000-000000000001"),
            ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
            SourceDocumentId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001"),
            SourceEventId = Guid.Parse("cccccccc-0000-4000-8000-000000000001"),
            ApprovedAtUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 3, 2, 10, 0, 5, DateTimeKind.Utc)
        };

        var older = new MilestoneSetup
        {
            Id = Guid.Parse("11111111-0000-4000-8000-000000000002"),
            ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"),
            SourceDocumentId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"),
            SourceEventId = Guid.Parse("cccccccc-0000-4000-8000-000000000002"),
            ApprovedAtUtc = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 3, 1, 9, 0, 5, DateTimeKind.Utc)
        };

        // The repository hands rows back newest-first; the controller passes that order through.
        var repository = new FakeMilestoneSetupRepository { Rows = [newer, older] };
        var controller = new MilestoneSetupsController(repository);

        var result = Assert.IsType<OkObjectResult>(await controller.List(default));
        var rows = Assert.IsAssignableFrom<IEnumerable<MilestoneSetupResponse>>(result.Value).ToList();

        Assert.Equal(2, rows.Count);

        Assert.Equal(newer.Id, rows[0].Id);
        Assert.Equal(newer.ProjectId, rows[0].ProjectId);
        Assert.Equal(newer.SourceDocumentId, rows[0].SourceDocumentId);
        Assert.Equal(newer.SourceEventId, rows[0].SourceEventId);
        Assert.Equal(newer.ApprovedAtUtc, rows[0].ApprovedAtUtc);
        Assert.Equal(newer.CreatedAtUtc, rows[0].CreatedAtUtc);

        Assert.Equal(older.Id, rows[1].Id);
    }

    [Fact]
    public async Task List_returns_an_empty_list_when_nothing_has_been_approved_yet()
    {
        var controller = new MilestoneSetupsController(new FakeMilestoneSetupRepository());

        var result = Assert.IsType<OkObjectResult>(await controller.List(default));

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<MilestoneSetupResponse>>(result.Value));
    }
}
