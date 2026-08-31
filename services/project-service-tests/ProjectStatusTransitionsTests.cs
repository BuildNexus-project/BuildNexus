using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The third US-06 acceptance bullet on its own: a project's status only moves
/// through valid transitions.
/// </summary>
/// <remarks>
/// Pure rules, so these need neither MySQL nor a broker. The lifecycle is
/// <c>Pending → Designing → DesignApproved → Construction → Completed</c>, and
/// what makes it worth testing is everything it must refuse, not the four moves
/// it allows.
/// </remarks>
public class ProjectStatusTransitionsTests
{
    [Theory]
    [InlineData(ProjectStatus.Pending, ProjectStatus.Designing)]
    [InlineData(ProjectStatus.Designing, ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.DesignApproved, ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Construction, ProjectStatus.Completed)]
    public void Allows_each_step_of_the_lifecycle(ProjectStatus from, ProjectStatus to)
    {
        Assert.True(ProjectStatusTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(ProjectStatus.Pending, ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.Pending, ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Pending, ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Designing, ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Designing, ProjectStatus.Completed)]
    [InlineData(ProjectStatus.DesignApproved, ProjectStatus.Completed)]
    public void Refuses_a_move_that_skips_a_stage(ProjectStatus from, ProjectStatus to)
    {
        // A build must not start before its design is approved, and a project
        // must not be declared finished before it has been built.
        Assert.False(ProjectStatusTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(ProjectStatus.Designing, ProjectStatus.Pending)]
    [InlineData(ProjectStatus.DesignApproved, ProjectStatus.Designing)]
    [InlineData(ProjectStatus.Construction, ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Pending)]
    public void Refuses_a_move_backwards(ProjectStatus from, ProjectStatus to)
    {
        // The history is the record of what happened, not somewhere to undo it.
        Assert.False(ProjectStatusTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(ProjectStatus.Pending)]
    [InlineData(ProjectStatus.Designing)]
    [InlineData(ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Completed)]
    public void Refuses_a_move_to_the_status_the_project_already_holds(ProjectStatus status)
    {
        // A no-op change would write a history entry saying nothing happened.
        Assert.False(ProjectStatusTransitions.IsAllowed(status, status));
    }

    [Fact]
    public void Nothing_follows_a_completed_project()
    {
        Assert.Empty(ProjectStatusTransitions.NextFrom(ProjectStatus.Completed));
    }

    [Theory]
    [InlineData(ProjectStatus.Pending, ProjectStatus.Designing)]
    [InlineData(ProjectStatus.Designing, ProjectStatus.DesignApproved)]
    [InlineData(ProjectStatus.DesignApproved, ProjectStatus.Construction)]
    [InlineData(ProjectStatus.Construction, ProjectStatus.Completed)]
    public void Offers_exactly_one_next_status_at_every_stage_before_the_end(
        ProjectStatus current,
        ProjectStatus expected)
    {
        // This is what the UI offers as buttons, so "one" matters as much as
        // "which": a screen cannot present a choice the lifecycle does not have.
        Assert.Equal([expected], ProjectStatusTransitions.NextFrom(current));
    }

    [Fact]
    public void Every_status_has_an_answer()
    {
        // Written over the enum rather than over today's five names, so a status
        // added in a later story and left out of the transition table fails here
        // instead of quietly becoming a dead end nobody can move a project out
        // of.
        var dead = Enum.GetValues<ProjectStatus>()
            .Where(status => status != ProjectStatus.Completed)
            .Where(status => ProjectStatusTransitions.NextFrom(status).Count == 0)
            .ToList();

        Assert.True(
            dead.Count == 0,
            "Every status except Completed must have somewhere to go. These do not: "
            + string.Join(", ", dead));
    }

    [Fact]
    public void No_status_can_move_to_itself_or_to_something_that_is_not_a_status()
    {
        // The whole table swept, so an entry added carelessly later cannot
        // introduce a self-loop or a value outside the enum.
        foreach (var from in Enum.GetValues<ProjectStatus>())
        {
            foreach (var to in ProjectStatusTransitions.NextFrom(from))
            {
                Assert.NotEqual(from, to);
                Assert.True(Enum.IsDefined(to));
            }
        }
    }
}
