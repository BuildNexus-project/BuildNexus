using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Controllers;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The US-38 acceptance criteria walked over stand-in collaborators: every
/// project is listed with its status, assigned staff and last-updated date, and
/// the stalled ones are flagged.
/// </summary>
/// <remarks>
/// Who may call the endpoint is the role attribute's decision, pinned in
/// <c>EndpointRoleDeclarationTests</c>; here the caller is simply the Admin the
/// route lets through.
/// </remarks>
public class ProjectOversightEndpointTests
{
    private static readonly Guid ArchitectId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ManagerId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    // ------------------------------------------- AC-1: system-wide list ----

    [Fact]
    public async Task Lists_every_project_with_its_status_and_last_updated_date()
    {
        var updated = DateTime.UtcNow.AddDays(-1);
        var (controller, _) = ControllerWith(
            Project("Beachfront villa", ProjectStatus.Designing, updatedAt: updated),
            Project("Hilltop cottage", ProjectStatus.Construction),
            Project("Town house", ProjectStatus.Completed));

        var response = await Oversight(controller);

        Assert.Equal(3, response.TotalProjects);
        Assert.Equal(["Beachfront villa", "Hilltop cottage", "Town house"], response.Projects.Select(p => p.Name));
        Assert.Equal(["Designing", "Construction", "Completed"], response.Projects.Select(p => p.Status));
        Assert.Equal(updated, response.Projects[0].UpdatedAt);
    }

    [Fact]
    public async Task Leaves_cancelled_projects_in_the_list()
    {
        // GET /api/projects hides them by default; the oversight view is the whole picture.
        var (controller, _) = ControllerWith(
            Project("Active", ProjectStatus.Designing),
            Project("Closed out", ProjectStatus.Cancelled));

        var response = await Oversight(controller);

        Assert.Contains(response.Projects, p => p.Status == "Cancelled");
    }

    [Fact]
    public async Task Carries_the_assigned_staff_by_id_and_by_name()
    {
        var (controller, resolver) = ControllerWith(
            Project("Staffed", ProjectStatus.Construction, architect: ArchitectId, manager: ManagerId));
        resolver.Names[ArchitectId] = "Amara Perera";
        resolver.Names[ManagerId] = "Nuwan Silva";

        var row = Assert.Single((await Oversight(controller)).Projects);

        Assert.Equal(ArchitectId, row.AssignedArchitectId);
        Assert.Equal("Amara Perera", row.AssignedArchitectName);
        Assert.Equal(ManagerId, row.AssignedProjectManagerId);
        Assert.Equal("Nuwan Silva", row.AssignedProjectManagerName);
    }

    [Fact]
    public async Task A_project_nobody_is_on_has_no_staff()
    {
        var (controller, _) = ControllerWith(Project("Unstaffed", ProjectStatus.Pending));

        var row = Assert.Single((await Oversight(controller)).Projects);

        Assert.Null(row.AssignedArchitectId);
        Assert.Null(row.AssignedArchitectName);
        Assert.Null(row.AssignedProjectManagerId);
        Assert.Null(row.AssignedProjectManagerName);
    }

    [Fact]
    public async Task A_name_that_cannot_be_found_leaves_the_id_in_place_and_the_list_intact()
    {
        // The resolver knows nobody — what it answers when the User Service is down.
        var (controller, _) = ControllerWith(
            Project("Staffed", ProjectStatus.Designing, architect: ArchitectId));

        var row = Assert.Single((await Oversight(controller)).Projects);

        Assert.Equal(ArchitectId, row.AssignedArchitectId);
        Assert.Null(row.AssignedArchitectName);
    }

    [Fact]
    public async Task Asks_for_each_assigned_person_once_however_many_projects_they_are_on()
    {
        var (controller, resolver) = ControllerWith(
            Project("One", ProjectStatus.Designing, architect: ArchitectId, manager: ManagerId),
            Project("Two", ProjectStatus.Designing, architect: ArchitectId),
            Project("Three", ProjectStatus.Pending));

        await Oversight(controller);

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(2, resolver.LastAsked.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, resolver.LastAsked);
    }

    [Fact]
    public async Task An_empty_platform_is_an_empty_list()
    {
        var (controller, _) = ControllerWith();

        var response = await Oversight(controller);

        Assert.Equal(0, response.TotalProjects);
        Assert.Equal(0, response.StalledCount);
        Assert.Empty(response.Projects);
    }

    [Fact]
    public async Task Says_when_the_list_was_generated()
    {
        var (controller, _) = ControllerWith();
        var before = DateTime.UtcNow;

        var response = await Oversight(controller);

        Assert.InRange(response.GeneratedAt, before, DateTime.UtcNow);
    }

    // ------------------------------------------- AC-2: stalled projects ----

    [Fact]
    public async Task Flags_an_open_project_that_has_not_moved_for_a_fortnight()
    {
        var (controller, _) = ControllerWith(
            Project("Forgotten", ProjectStatus.Designing, updatedAt: DateTime.UtcNow.AddDays(-30)),
            Project("Busy", ProjectStatus.Designing, updatedAt: DateTime.UtcNow.AddDays(-1)),
            Project("Finished long ago", ProjectStatus.Completed, updatedAt: DateTime.UtcNow.AddDays(-300)));

        var response = await Oversight(controller);

        Assert.True(Assert.Single(response.Projects, p => p.Name == "Forgotten").IsStalled);
        Assert.False(Assert.Single(response.Projects, p => p.Name == "Busy").IsStalled);
        Assert.False(Assert.Single(response.Projects, p => p.Name == "Finished long ago").IsStalled);
        Assert.Equal(1, response.StalledCount);
    }

    [Fact]
    public async Task Tells_the_screen_the_threshold_so_it_keeps_no_copy_of_it()
    {
        var (controller, _) = ControllerWith();

        var response = await Oversight(controller);

        Assert.Equal(ProjectStallPolicy.StalledAfterDays, response.StalledAfterDays);
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<ProjectOversightResponse> Oversight(OversightController controller) =>
        Assert.IsType<ProjectOversightResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetOversight(default)).Value);

    private static (OversightController Controller, FakeUserNameResolver Resolver) ControllerWith(
        params Project[] projects)
    {
        var resolver = new FakeUserNameResolver();

        return (new OversightController(new ListingRepository(projects), resolver), resolver);
    }

    private static Project Project(
        string name,
        ProjectStatus status,
        DateTime? updatedAt = null,
        Guid? architect = null,
        Guid? manager = null) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = Guid.NewGuid(),
        Name = name,
        Location = "Galle",
        Status = status,
        AssignedArchitectId = architect,
        AssignedProjectManagerId = manager,
        CreatedAt = DateTime.UtcNow.AddDays(-60),
        UpdatedAt = updatedAt ?? DateTime.UtcNow.AddDays(-1)
    };

    /// <summary>
    /// A repository that only lists. Everything else throws, so a change that
    /// starts reading or writing anything more from this endpoint cannot pass
    /// unnoticed.
    /// </summary>
    private sealed class ListingRepository(IReadOnlyList<Project> projects) : IProjectRepository
    {
        public Task<IReadOnlyList<Project>> ListAllAsync() => Task.FromResult(projects);

        public Task InsertAsync(Project project, ProjectStatusChange creation, IReadOnlyList<OutboxEvent> outboxEvents) =>
            throw new NotSupportedException();

        public Task<Project?> GetByIdAsync(Guid id) => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListForUserAsync(Guid userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectStatusChange>> GetStatusHistoryAsync(Guid projectId) =>
            throw new NotSupportedException();

        public Task<bool> UpdateStatusAsync(
            ProjectStatusChange change,
            DateTime updatedAtUtc,
            IReadOnlyList<OutboxEvent> outboxEvents) => throw new NotSupportedException();

        public Task<bool> UpdatePaymentStatusAsync(
            Guid projectId,
            ProjectPaymentStatus paymentStatus,
            Guid sourceEventId,
            DateTime occurredAtUtc) => throw new NotSupportedException();

        public Task<bool> AssignArchitectAsync(
            Guid projectId,
            Guid architectId,
            ProjectStatusChange? transition,
            IReadOnlyList<OutboxEvent> outboxEvents,
            DateTime updatedAtUtc) => throw new NotSupportedException();

        public Task<bool> AssignProjectManagerAsync(
            Guid projectId,
            Guid projectManagerId,
            DateTime updatedAtUtc) => throw new NotSupportedException();
    }
}
