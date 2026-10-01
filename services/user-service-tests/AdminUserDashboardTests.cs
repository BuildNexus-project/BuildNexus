using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Controllers;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The user half of US-21 AC-4 walked over a stand-in query: an Admin sees how
/// many accounts the system holds, how many can sign in, and how they divide by
/// role. The repository is a stand-in, so these run without a database.
/// </summary>
/// <remarks>
/// The role gate in front of the action is not retested here — it is declared on
/// the controller and pinned by reflection in <see cref="EndpointRoleDeclarationTests"/>.
/// The project count on the same dashboard is the Project Service's data and is
/// tested there.
/// </remarks>
public class AdminUserDashboardTests
{
    [Fact]
    public async Task Reports_the_total_number_of_accounts()
    {
        var dashboard = await DashboardOf(
            Group(UserRole.Client, active: true, 4),
            Group(UserRole.Architect, active: true, 2),
            Group(UserRole.ProjectManager, active: true, 1),
            Group(UserRole.Admin, active: true, 1));

        Assert.Equal(8, dashboard.TotalUsers);
    }

    [Fact]
    public async Task Splits_the_total_into_accounts_that_can_sign_in_and_accounts_that_cannot()
    {
        var dashboard = await DashboardOf(
            Group(UserRole.Client, active: true, 4),
            Group(UserRole.Client, active: false, 2),
            Group(UserRole.Architect, active: false, 1));

        Assert.Equal(7, dashboard.TotalUsers);
        Assert.Equal(4, dashboard.ActiveUsers);
        Assert.Equal(3, dashboard.InactiveUsers);
    }

    [Fact]
    public async Task Counts_each_role_whether_or_not_its_accounts_are_active()
    {
        var dashboard = await DashboardOf(
            Group(UserRole.Client, active: true, 4),
            Group(UserRole.Client, active: false, 2),
            Group(UserRole.Architect, active: true, 3));

        Assert.Equal(6, RoleCount(dashboard, "Client"));
        Assert.Equal(3, RoleCount(dashboard, "Architect"));
    }

    [Fact]
    public async Task Lists_every_role_in_platform_order_even_those_nobody_holds()
    {
        var dashboard = await DashboardOf(Group(UserRole.Client, active: true, 1));

        Assert.Equal(
            ["Client", "Architect", "ProjectManager", "Admin"],
            dashboard.Roles.Select(role => role.Role));
        Assert.Equal(0, RoleCount(dashboard, "ProjectManager"));
    }

    [Fact]
    public async Task The_role_counts_add_up_to_the_total()
    {
        var dashboard = await DashboardOf(
            Group(UserRole.Client, active: true, 5),
            Group(UserRole.Client, active: false, 1),
            Group(UserRole.ProjectManager, active: true, 2),
            Group(UserRole.Admin, active: true, 1));

        Assert.Equal(dashboard.TotalUsers, dashboard.Roles.Sum(role => role.Count));
    }

    [Fact]
    public async Task A_system_with_no_accounts_is_a_dashboard_of_zeros_not_an_error()
    {
        var dashboard = await DashboardOf();

        Assert.Equal(0, dashboard.TotalUsers);
        Assert.Equal(0, dashboard.ActiveUsers);
        Assert.Equal(0, dashboard.InactiveUsers);
        Assert.Equal(4, dashboard.Roles.Count);
        Assert.All(dashboard.Roles, role => Assert.Equal(0, role.Count));
    }

    // ------------------------------------------------------------ helpers ----

    private static async Task<AdminUsersDashboardResponse> DashboardOf(params UserCountGroup[] groups)
    {
        var controller = new DashboardController(new StubUserDashboardRepository(groups));

        var result = await controller.GetAdminDashboard(default);

        return Assert.IsType<AdminUsersDashboardResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }

    private static int RoleCount(AdminUsersDashboardResponse dashboard, string role) =>
        Assert.Single(dashboard.Roles, entry => entry.Role == role).Count;

    private static UserCountGroup Group(UserRole role, bool active, int count) =>
        new() { Role = role, IsActive = active, Count = count };

    /// <summary>Hands back whatever it was given. The grouping is SQL; the database tests cover it.</summary>
    private sealed class StubUserDashboardRepository : IUserDashboardRepository
    {
        private readonly IReadOnlyList<UserCountGroup> _groups;

        public StubUserDashboardRepository(IReadOnlyList<UserCountGroup> groups)
        {
            _groups = groups;
        }

        public Task<IReadOnlyList<UserCountGroup>> CountByRoleAndStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(_groups);
    }
}
