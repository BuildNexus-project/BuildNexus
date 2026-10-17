using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="InvoicesController"/> asks of the invoice repository, checked
/// with a Moq mock (US-15, AC-2).
/// </summary>
/// <remarks>
/// The repository is a stub where the test needs an answer back and a mock where
/// the test needs to know whether it was called at all. The invalid-request and
/// no-subject paths are the point: they must stop before anything is stored.
/// </remarks>
public class InvoicesControllerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly Mock<IInvoiceRepository> _invoices = new();

    public static TheoryData<decimal> AmountsThatPassValidation => new()
    {
        0.01m,
        1m,
        999.99m,
        250_000.75m,
        9_999_999_999_999.99m
    };

    [Fact]
    public async Task A_generated_invoice_is_created_once_for_the_route_project_and_the_caller()
    {
        _invoices
            .Setup(i => i.CreateAsync(ProjectId, 250_000.75m, ManagerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingInvoice(250_000.75m));

        var result = await Controller(ManagerId).Generate(
            ProjectId, new GenerateInvoiceRequest { Amount = 250_000.75m }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<InvoiceResponse>(created.Value);
        Assert.Equal(250_000.75m, body.Amount);
        Assert.Equal(InvoiceStatus.Pending, body.Status);

        _invoices.Verify(
            i => i.CreateAsync(ProjectId, 250_000.75m, ManagerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(AmountsThatPassValidation))]
    public async Task The_amount_reaches_the_repository_exactly_as_it_was_typed(decimal amount)
    {
        _invoices
            .Setup(i => i.CreateAsync(ProjectId, amount, ManagerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingInvoice(amount));

        await Controller(ManagerId).Generate(ProjectId, new GenerateInvoiceRequest { Amount = amount }, default);

        _invoices.Verify(
            i => i.CreateAsync(ProjectId, amount, ManagerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_invalid_amount_is_refused_and_nothing_is_stored()
    {
        var controller = Controller(ManagerId);
        controller.ModelState.AddModelError(
            nameof(GenerateInvoiceRequest.Amount), "The amount must be greater than zero.");

        var result = await controller.Generate(ProjectId, new GenerateInvoiceRequest { Amount = 0m }, default);

        Assert.IsType<ValidationProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        _invoices.Verify(
            i => i.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_cannot_raise_an_invoice()
    {
        var result = await Controller(callerId: null).Generate(
            ProjectId, new GenerateInvoiceRequest { Amount = 100m }, default);

        Assert.IsType<UnauthorizedResult>(result);
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_project_that_has_not_been_billed_lists_as_an_empty_list_not_an_error()
    {
        _invoices
            .Setup(i => i.ListForProjectAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Invoice>());

        var result = await Controller(ManagerId).List(ProjectId, default);

        var body = Assert.IsAssignableFrom<IReadOnlyList<InvoiceResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(body);
        _invoices.Verify(i => i.ListForProjectAsync(ProjectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private InvoicesController Controller(Guid? callerId) =>
        new(_invoices.Object) { ControllerContext = CallerContext.For(callerId, PlatformRoles.ProjectManager) };

    private static Invoice PendingInvoice(decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        Amount = amount,
        Status = InvoiceStatus.Pending,
        CreatedBy = ManagerId,
        CreatedAtUtc = new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc)
    };
}
