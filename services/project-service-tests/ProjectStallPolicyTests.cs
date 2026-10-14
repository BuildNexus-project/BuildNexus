using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The US-38 stalled-project rule on its own: an open project with no movement
/// for a fortnight is stalled, and nothing else is.
/// </summary>
public class ProjectStallPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_threshold_is_a_fortnight()
    {
        // The screen quotes this number to the Admin, so it is pinned.
        Assert.Equal(14, ProjectStallPolicy.StalledAfterDays);
    }

    [Theory]
    [InlineData(ProjectStatus.Pending)]
    [InlineData(ProjectStatus.Designing)]
    [InlineData(ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.Construction)]
    public void An_open_project_with_no_movement_for_longer_than_the_threshold_is_stalled(ProjectStatus status)
    {
        Assert.True(ProjectStallPolicy.IsStalled(status, Now.AddDays(-30), Now));
    }

    [Theory]
    [InlineData(ProjectStatus.Pending)]
    [InlineData(ProjectStatus.Designing)]
    [InlineData(ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.Construction)]
    public void An_open_project_that_moved_recently_is_not_stalled(ProjectStatus status)
    {
        Assert.False(ProjectStallPolicy.IsStalled(status, Now.AddDays(-2), Now));
    }

    [Fact]
    public void A_project_is_stalled_on_the_threshold_itself_but_not_a_moment_before()
    {
        Assert.True(ProjectStallPolicy.IsStalled(ProjectStatus.Designing, Now.AddDays(-14), Now));
        Assert.False(ProjectStallPolicy.IsStalled(ProjectStatus.Designing, Now.AddDays(-14).AddSeconds(1), Now));
    }

    [Theory]
    [InlineData(ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Cancelled)]
    public void A_finished_project_is_never_stalled_however_long_ago_it_moved(ProjectStatus status)
    {
        // Nobody is waiting on a project that is done or closed out.
        Assert.False(ProjectStallPolicy.IsStalled(status, Now.AddDays(-365), Now));
    }

    [Fact]
    public void A_project_updated_in_the_future_is_not_stalled()
    {
        // A clock a little behind the one that wrote the row must not flag it.
        Assert.False(ProjectStallPolicy.IsStalled(ProjectStatus.Designing, Now.AddMinutes(5), Now));
    }
}
