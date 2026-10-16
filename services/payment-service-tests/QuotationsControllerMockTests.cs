using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="QuotationsController"/> asks of the quotation repository,
/// checked with a Moq mock (US-15, AC-1).
/// </summary>
public class QuotationsControllerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly Mock<IQuotationRepository> _quotations = new();

    public static TheoryData<decimal> TotalsThatPassValidation => new()
    {
        0.01m,
        1m,
        900_000m,
        1_100_000.50m,
        9_999_999_999_999.99m
    };

    [Fact]
    public async Task A_generated_quotation_is_created_once_for_the_route_project_and_the_caller()
    {
        _quotations
            .Setup(q => q.CreateAsync(ProjectId, 900_000m, ManagerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(QuotationOf(900_000m));

        var result = await Controller(ManagerId).Generate(
            ProjectId, new GenerateQuotationRequest { EstimatedTotal = 900_000m }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(900_000m, Assert.IsType<QuotationResponse>(created.Value).EstimatedTotal);

        _quotations.Verify(
            q => q.CreateAsync(ProjectId, 900_000m, ManagerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(TotalsThatPassValidation))]
    public async Task The_estimated_total_reaches_the_repository_exactly_as_it_was_typed(decimal total)
    {
        _quotations
            .Setup(q => q.CreateAsync(ProjectId, total, ManagerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(QuotationOf(total));

        await Controller(ManagerId).Generate(
            ProjectId, new GenerateQuotationRequest { EstimatedTotal = total }, default);

        _quotations.Verify(
            q => q.CreateAsync(ProjectId, total, ManagerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Re_quoting_a_project_adds_a_second_quotation_and_never_changes_the_first()
    {
        // The earlier figure is the record of what the Client was told. The
        // controller may only ever insert: no other repository call is allowed.
        _quotations
            .Setup(q => q.CreateAsync(
                ProjectId, It.IsAny<decimal>(), ManagerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(QuotationOf(900_000m));
        var controller = Controller(ManagerId);

        await controller.Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 900_000m }, default);
        await controller.Generate(ProjectId, new GenerateQuotationRequest { EstimatedTotal = 1_100_000m }, default);

        _quotations.Verify(
            q => q.CreateAsync(ProjectId, It.IsAny<decimal>(), ManagerId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _quotations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_total_is_refused_and_nothing_is_stored()
    {
        var controller = Controller(ManagerId);
        controller.ModelState.AddModelError(
            nameof(GenerateQuotationRequest.EstimatedTotal), "The estimated total must be greater than zero.");

        var result = await controller.Generate(
            ProjectId, new GenerateQuotationRequest { EstimatedTotal = -5m }, default);

        Assert.IsType<ValidationProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        _quotations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_cannot_raise_a_quotation()
    {
        var result = await Controller(callerId: null).Generate(
            ProjectId, new GenerateQuotationRequest { EstimatedTotal = 500_000m }, default);

        Assert.IsType<UnauthorizedResult>(result);
        _quotations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_project_that_was_never_quoted_lists_as_an_empty_list_not_an_error()
    {
        _quotations
            .Setup(q => q.ListForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Quotation>());

        var result = await Controller(ManagerId).List(ProjectId, default);

        var body = Assert.IsAssignableFrom<IReadOnlyList<QuotationResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(body);
    }

    private QuotationsController Controller(Guid? callerId) =>
        new(_quotations.Object) { ControllerContext = CallerContext.For(callerId, PlatformRoles.ProjectManager) };

    private static Quotation QuotationOf(decimal total) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        EstimatedTotal = total,
        CreatedBy = ManagerId,
        CreatedAtUtc = new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc)
    };
}
