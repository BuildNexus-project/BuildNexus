using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The one definition of a build's progress percentage, shared by the per-project screen,
/// the construction report and the role dashboards.
/// </summary>
/// <remarks>
/// Each of those used to carry its own private copy. Sharing it means a project's figure is
/// the same number wherever it is shown, and these tests pin the arithmetic — including the
/// rounding — so a change to it is a decision made here rather than an accident in one of
/// three places.
/// </remarks>
public class ProgressPercentageTests
{
    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 25)]
    [InlineData(2, 4, 50)]
    [InlineData(3, 4, 75)]
    [InlineData(4, 4, 100)]
    public void Is_completed_over_total_as_a_percentage(int completed, int total, int expected)
    {
        Assert.Equal((decimal)expected, ProgressPercentage.Of(completed, total));
    }

    [Theory]
    [InlineData(1, 3, "33.33")]
    [InlineData(2, 3, "66.67")]
    [InlineData(1, 7, "14.29")]
    [InlineData(5, 6, "83.33")]
    public void Is_rounded_to_two_decimal_places(int completed, int total, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ProgressPercentage.Of(completed, total));
    }

    [Fact]
    public void Is_zero_when_nothing_is_planned_rather_than_a_division_by_zero()
    {
        // A project that is approved but has no milestones yet is a real state, not an error.
        Assert.Equal(0m, ProgressPercentage.Of(0, 0));
    }

    [Fact]
    public void Rounds_half_to_even_exactly_as_every_caller_did_before_it_was_shared()
    {
        // 1 of 800 is exactly 0.125%. Half-to-even rounds it to 0.12; half-away-from-zero
        // would give 0.13. This is the behaviour each private copy had, pinned so that
        // sharing the calculation provably changed no figure anywhere.
        Assert.Equal(0.12m, ProgressPercentage.Of(1, 800));
    }

    [Fact]
    public void Never_exceeds_one_hundred_when_everything_is_done()
    {
        Assert.Equal(100m, ProgressPercentage.Of(9, 9));
    }
}
