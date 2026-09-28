using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// <see cref="ReportsController"/> over a stand-in repository: the totals reaching the wire,
/// the optional date range reaching the repository, and the one request shape it refuses
/// (US-19 AC-2).
/// </summary>
/// <remarks>
/// The role gating is pinned by <see cref="EndpointRoleDeclarationTests"/>; the aggregates
/// themselves by <see cref="PaymentReportRepositoryDatabaseTests"/>.
/// </remarks>
public class ReportsControllerTests
{
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Returns_the_portfolio_totals()
    {
        var repository = new FakePaymentReportRepository
        {
            Summary = new PaymentReportSummary
            {
                TotalInvoiced = 18_500_000m,
                TotalCollected = 12_000_000m,
                TotalOutstanding = 6_500_000m,
                InvoiceCount = 7,
                PaymentCount = 4
            }
        };

        var result = await new ReportsController(repository).GetSummary(null, null, default);

        var body = Assert.IsType<PaymentReportSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);

        Assert.Equal(18_500_000m, body.TotalInvoiced);
        Assert.Equal(12_000_000m, body.TotalCollected);
        Assert.Equal(6_500_000m, body.TotalOutstanding);
        // The counts let a reader tell one big invoice from fifty small ones.
        Assert.Equal(7, body.InvoiceCount);
        Assert.Equal(4, body.PaymentCount);
        // Unfiltered, so no range is echoed back.
        Assert.Null(body.FromUtc);
        Assert.Null(body.ToUtc);
    }

    [Fact]
    public async Task An_empty_ledger_is_zeros_not_an_error()
    {
        // A young portfolio that has billed nothing yet. Failing here would leave a caller
        // unable to tell "nothing billed" from "the report is broken".
        var result = await new ReportsController(new FakePaymentReportRepository()).GetSummary(null, null, default);

        var body = Assert.IsType<PaymentReportSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);

        Assert.Equal(0m, body.TotalInvoiced);
        Assert.Equal(0m, body.TotalCollected);
        Assert.Equal(0, body.InvoiceCount);
    }

    [Fact]
    public async Task The_date_range_reaches_the_repository_untouched()
    {
        var repository = new FakePaymentReportRepository();

        await new ReportsController(repository).GetSummary(From, To, default);

        var call = Assert.Single(repository.Calls);
        Assert.Equal(From, call.FromUtc);
        Assert.Equal(To, call.ToUtc);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Either_bound_may_be_given_on_its_own(bool withFrom, bool withTo)
    {
        // The two bounds are independent: "everything since March" and "everything up to
        // March" are both reasonable asks, and neither requires the other.
        var repository = new FakePaymentReportRepository();

        var result = await new ReportsController(repository)
            .GetSummary(withFrom ? From : null, withTo ? To : null, default);

        Assert.IsType<OkObjectResult>(result);
        var call = Assert.Single(repository.Calls);
        Assert.Equal(withFrom ? From : null, call.FromUtc);
        Assert.Equal(withTo ? To : null, call.ToUtc);
    }

    [Fact]
    public async Task An_inverted_range_is_refused_rather_than_answered_with_zeros()
    {
        // The window is empty by construction, so the query would honestly return zeros —
        // and a reader would take that as "nothing was billed". Naming the mistake beats
        // answering a question the caller did not mean to ask.
        var repository = new FakePaymentReportRepository();

        var result = await new ReportsController(repository).GetSummary(To, From, default);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Invalid date range.", problem.Title);
        // And nothing was queried.
        Assert.Empty(repository.Calls);
    }

    [Fact]
    public async Task An_equal_pair_of_bounds_is_refused_too()
    {
        // toUtc is exclusive, so from == to selects nothing at all.
        var result = await new ReportsController(new FakePaymentReportRepository())
            .GetSummary(From, From, default);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_filtered_summary_carries_a_null_outstanding_through()
    {
        // The repository decides this; the controller must not invent a number to fill the
        // gap, because invoiced and collected are scoped by different timestamps here.
        var repository = new FakePaymentReportRepository
        {
            Summary = new PaymentReportSummary
            {
                TotalInvoiced = 5_000_000m,
                TotalCollected = 1_000_000m,
                TotalOutstanding = null,
                InvoiceCount = 2,
                PaymentCount = 1,
                FromUtc = From,
                ToUtc = To
            }
        };

        var result = await new ReportsController(repository).GetSummary(From, To, default);

        var body = Assert.IsType<PaymentReportSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);

        Assert.Null(body.TotalOutstanding);
        Assert.Equal(From, body.FromUtc);
        Assert.Equal(To, body.ToUtc);
    }
}
