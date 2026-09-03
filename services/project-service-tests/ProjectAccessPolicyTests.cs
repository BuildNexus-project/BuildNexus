using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The second US-06 acceptance bullet on its own: access is restricted to the
/// owning client, assigned staff, or an Admin.
/// </summary>
/// <remarks>
/// The rule <c>[Authorize(Roles = ...)]</c> cannot express. The attribute
/// answers "may an Architect call this endpoint"; this answers "may
/// <em>this</em> Architect open <em>that</em> project", and holding the right
/// role while being on somebody else's project is exactly the case that has to
/// be refused.
/// </remarks>
public class ProjectAccessPolicyTests
{
    private static readonly Guid Owner = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly Guid Architect = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProjectManager = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Outsider = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void The_client_who_submitted_it_may_view_their_own_project()
    {
        Assert.True(ProjectAccessPolicy.CanView(Staffed(), Owner, PlatformRoles.Client));
    }

    [Fact]
    public void The_assigned_architect_may_view_it()
    {
        Assert.True(ProjectAccessPolicy.CanView(Staffed(), Architect, PlatformRoles.Architect));
    }

    [Fact]
    public void The_assigned_project_manager_may_view_it()
    {
        Assert.True(ProjectAccessPolicy.CanView(Staffed(), ProjectManager, PlatformRoles.ProjectManager));
    }

    [Fact]
    public void An_admin_may_view_a_project_they_have_nothing_to_do_with()
    {
        Assert.True(ProjectAccessPolicy.CanView(Staffed(), Outsider, PlatformRoles.Admin));
    }

    [Theory]
    [InlineData(PlatformRoles.Client)]
    [InlineData(PlatformRoles.Architect)]
    [InlineData(PlatformRoles.ProjectManager)]
    public void Somebody_who_is_not_on_the_project_may_not_view_it(string role)
    {
        // The point of the whole policy: an Architect is not this project's
        // Architect, and another Client's project is not theirs to read.
        Assert.False(ProjectAccessPolicy.CanView(Staffed(), Outsider, role));
    }

    [Fact]
    public void Nobody_but_an_admin_reaches_an_unstaffed_project_they_did_not_submit()
    {
        // Which is every project today — nothing assigns staff yet — so this
        // pins what that actually means rather than leaving it to be discovered.
        var unstaffed = Project();

        Assert.False(ProjectAccessPolicy.CanView(unstaffed, Architect, PlatformRoles.Architect));
        Assert.False(ProjectAccessPolicy.CanView(unstaffed, ProjectManager, PlatformRoles.ProjectManager));
        Assert.True(ProjectAccessPolicy.CanView(unstaffed, Owner, PlatformRoles.Client));
        Assert.True(ProjectAccessPolicy.CanView(unstaffed, Outsider, PlatformRoles.Admin));
    }

    [Fact]
    public void The_owning_client_may_not_change_their_own_projects_status()
    {
        // They can see every step of it, but declaring the design approved or
        // the build finished is the company's word, not the customer's. This is
        // the one case where viewing and changing part ways.
        var project = Staffed();

        Assert.True(ProjectAccessPolicy.CanView(project, Owner, PlatformRoles.Client));
        Assert.False(ProjectAccessPolicy.CanUpdateStatus(project, Owner, PlatformRoles.Client));
    }

    [Fact]
    public void The_assigned_staff_and_an_admin_may_change_the_status()
    {
        var project = Staffed();

        Assert.True(ProjectAccessPolicy.CanUpdateStatus(project, Architect, PlatformRoles.Architect));
        Assert.True(ProjectAccessPolicy.CanUpdateStatus(project, ProjectManager, PlatformRoles.ProjectManager));
        Assert.True(ProjectAccessPolicy.CanUpdateStatus(project, Outsider, PlatformRoles.Admin));
    }

    [Theory]
    [InlineData(PlatformRoles.Architect)]
    [InlineData(PlatformRoles.ProjectManager)]
    public void Staff_who_are_not_on_the_project_may_not_change_its_status(string role)
    {
        Assert.False(ProjectAccessPolicy.CanUpdateStatus(Staffed(), Outsider, role));
    }

    [Theory]
    [InlineData(PlatformRoles.Client)]
    [InlineData(PlatformRoles.Architect)]
    [InlineData(PlatformRoles.ProjectManager)]
    public void Only_an_admin_sees_every_project(string role)
    {
        Assert.False(ProjectAccessPolicy.SeesEveryProject(role));
        Assert.True(ProjectAccessPolicy.SeesEveryProject(PlatformRoles.Admin));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Administrator")]
    [InlineData("")]
    [InlineData(null)]
    public void A_role_that_is_not_exactly_Admin_is_not_an_admin(string? role)
    {
        // The role claim is a wire value the User Service signs, not free text.
        // Matching it loosely would accept a token this service should not be
        // trusting.
        Assert.False(ProjectAccessPolicy.SeesEveryProject(role));
        Assert.False(ProjectAccessPolicy.CanView(Staffed(), Outsider, role));
        Assert.False(ProjectAccessPolicy.CanUpdateStatus(Staffed(), Outsider, role));
    }

    /// <summary>A project nobody has been put on — every project today.</summary>
    private static Project Project() => new()
    {
        Id = Guid.Parse("b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b"),
        ClientId = Owner,
        Name = "Beachfront villa",
        Status = ProjectStatus.Pending
    };

    /// <summary>The same project with an Architect and a Project Manager on it.</summary>
    private static Project Staffed()
    {
        var project = Project();
        project.AssignedArchitectId = Architect;
        project.AssignedProjectManagerId = ProjectManager;

        return project;
    }
}
