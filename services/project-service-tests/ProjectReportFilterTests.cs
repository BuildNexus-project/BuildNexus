using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// Building the report's filter from a request (US-18 AC-2): what is accepted,
/// what is refused, and where the date range's edges fall. No database.
/// </summary>
public class ProjectReportFilterTests
{
    [Fact]
    public void No_arguments_is_the_whole_pipeline()
    {
        Assert.True(ProjectReportFilterParser.TryParse(null, null, null, out var filter, out var error));

        Assert.Null(error);
        Assert.Null(filter!.Statuses);
        Assert.Null(filter.From);
        Assert.Null(filter.To);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void Blank_status_values_mean_no_status_filter(string blank)
    {
        Assert.True(ProjectReportFilterParser.TryParse([blank], null, null, out var filter, out _));

        Assert.Null(filter!.Statuses);
    }

    [Fact]
    public void Repeated_and_comma_separated_statuses_mean_the_same_thing()
    {
        Assert.True(ProjectReportFilterParser.TryParse(
            ["Pending", "Designing"], null, null, out var repeated, out _));
        Assert.True(ProjectReportFilterParser.TryParse(
            ["Pending, Designing"], null, null, out var joined, out _));

        Assert.Equal([ProjectStatus.Pending, ProjectStatus.Designing], repeated!.Statuses);
        Assert.Equal(repeated.Statuses, joined!.Statuses);
    }

    [Fact]
    public void Status_names_are_not_case_sensitive()
    {
        Assert.True(ProjectReportFilterParser.TryParse(["designapproved"], null, null, out var filter, out _));

        Assert.Equal([ProjectStatus.DesignApproved], filter!.Statuses);
    }

    [Fact]
    public void A_status_named_twice_is_kept_once()
    {
        Assert.True(ProjectReportFilterParser.TryParse(
            ["Pending", "pending"], null, null, out var filter, out _));

        Assert.Equal([ProjectStatus.Pending], filter!.Statuses);
    }

    [Theory]
    [InlineData("Finished")]
    [InlineData("3")]
    [InlineData("Pending;Designing")]
    public void An_unknown_status_is_refused_and_the_message_names_the_valid_ones(string value)
    {
        Assert.False(ProjectReportFilterParser.TryParse([value], null, null, out var filter, out var error));

        Assert.Null(filter);
        Assert.Contains($"'{value}'", error);
        foreach (var name in Enum.GetNames<ProjectStatus>())
        {
            Assert.Contains(name, error);
        }
    }

    [Fact]
    public void One_bad_status_refuses_the_whole_filter_even_beside_good_ones()
    {
        Assert.False(ProjectReportFilterParser.TryParse(
            ["Pending", "Nonsense"], null, null, out var filter, out _));

        Assert.Null(filter);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_refused()
    {
        Assert.False(ProjectReportFilterParser.TryParse(
            null, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 9), out var filter, out var error));

        Assert.Null(filter);
        Assert.Contains("'from'", error);
    }

    [Fact]
    public void A_one_day_range_is_accepted()
    {
        var day = new DateOnly(2026, 9, 10);

        Assert.True(ProjectReportFilterParser.TryParse(null, day, day, out var filter, out _));

        Assert.Equal(day, filter!.From);
        Assert.Equal(day, filter.To);
    }

    [Fact]
    public void Either_end_of_the_range_may_be_left_open()
    {
        Assert.True(ProjectReportFilterParser.TryParse(null, new DateOnly(2026, 9, 1), null, out var fromOnly, out _));
        Assert.True(ProjectReportFilterParser.TryParse(null, null, new DateOnly(2026, 9, 1), out var toOnly, out _));

        Assert.NotNull(fromOnly!.FromInclusiveUtc);
        Assert.Null(fromOnly.ToExclusiveUtc);
        Assert.Null(toOnly!.FromInclusiveUtc);
        Assert.NotNull(toOnly.ToExclusiveUtc);
    }

    [Fact]
    public void The_range_covers_both_end_days_whole()
    {
        var filter = new ProjectReportFilter { From = new DateOnly(2026, 9, 1), To = new DateOnly(2026, 9, 30) };

        // The first instant of the first day is in; the first instant of the day
        // after the last is the exclusive upper bound, so 23:59:59.999 on the
        // last day is in and midnight after it is out.
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), filter.FromInclusiveUtc);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), filter.ToExclusiveUtc);
    }

    [Fact]
    public void The_upper_bound_rolls_over_a_month_and_year_end()
    {
        var filter = new ProjectReportFilter { To = new DateOnly(2026, 12, 31) };

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), filter.ToExclusiveUtc);
    }
}
