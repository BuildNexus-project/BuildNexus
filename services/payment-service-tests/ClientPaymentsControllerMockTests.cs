using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Contracts;
using BuildNexus.PaymentService.Controllers;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What <see cref="ClientPaymentsController"/> asks of its repositories, checked
/// with Moq mocks (US-16).
/// </summary>
/// <remarks>
/// The older <see cref="ClientPaymentsControllerTests"/> use hand-written fakes
/// that behave like a small database. These use mocks instead: each repository
/// is told what to answer (a stub), and the test then checks which calls were
/// made, how often, and with which arguments (<c>Verify</c>). That is the only
/// way to prove a call did <em>not</em> happen on a failure path.
/// </remarks>
public class ClientPaymentsControllerMockTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid InvoiceId = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IProjectOwnerRepository> _owners = new();

    public static TheoryData<decimal> AmountsThatPassValidation => new()
    {
        0.01m,
        1m,
        999.99m,
        250_000.75m,
        9_999_999_999_999.99m
    };

    [Fact]
    public async Task A_payment_on_an_owned_invoice_is_recorded_once_for_the_caller()
    {
        StubInvoiceOwnedByCaller();
        _payments
            .Setup(p => p.RecordPaymentAsync(InvoiceId, 400m, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRecordingResult.Recorded(PaymentOf(400m), 600m, InvoiceStatus.Pending));

        var result = await Controller(ClientId).Record(InvoiceId, Request(400m), default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<RecordPaymentResponse>(created.Value);
        Assert.Equal(400m, body.Payment.Amount);
        Assert.Equal(600m, body.OutstandingAmount);
        Assert.Equal(InvoiceStatus.Pending, body.InvoiceStatus);

        _payments.Verify(
            p => p.RecordPaymentAsync(InvoiceId, 400m, ClientId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(AmountsThatPassValidation))]
    public async Task The_amount_reaches_the_repository_exactly_as_the_client_sent_it(decimal amount)
    {
        // No rounding, no capping in the controller: the cap is the repository's
        // job, inside its transaction.
        StubInvoiceOwnedByCaller();
        _payments
            .Setup(p => p.RecordPaymentAsync(InvoiceId, amount, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRecordingResult.Recorded(PaymentOf(amount), 0m, InvoiceStatus.Paid));

        await Controller(ClientId).Record(InvoiceId, Request(amount), default);

        _payments.Verify(
            p => p.RecordPaymentAsync(InvoiceId, amount, ClientId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_client_who_does_not_own_the_project_never_reaches_the_payment_repository()
    {
        _invoices
            .Setup(i => i.GetAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceOnProject());
        _owners
            .Setup(o => o.IsOwnedByAsync(ProjectId, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Controller(ClientId).Record(InvoiceId, Request(100m), default);

        AssertForbidden(result);
        _payments.Verify(
            p => p.RecordPaymentAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_unknown_invoice_is_refused_without_asking_who_owns_it_or_paying_it()
    {
        _invoices
            .Setup(i => i.GetAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);

        var result = await Controller(ClientId).Record(InvoiceId, Request(100m), default);

        AssertForbidden(result);
        _owners.Verify(
            o => o.IsOwnedByAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_amount_is_refused_before_any_repository_is_touched()
    {
        var controller = Controller(ClientId);
        controller.ModelState.AddModelError(
            nameof(RecordPaymentRequest.Amount), "The amount must be greater than zero.");

        var result = await controller.Record(InvoiceId, Request(0m), default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);

        _invoices.VerifyNoOtherCalls();
        _owners.VerifyNoOtherCalls();
        _payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_token_without_a_usable_subject_is_unauthorized_and_nothing_is_looked_up()
    {
        var result = await Controller(callerId: null).Record(InvoiceId, Request(100m), default);

        Assert.IsType<UnauthorizedResult>(result);
        _invoices.VerifyNoOtherCalls();
        _payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invoice_deleted_between_the_two_reads_is_refused_like_one_that_is_not_theirs()
    {
        // The repository says "not found" after the controller had found it.
        StubInvoiceOwnedByCaller();
        _payments
            .Setup(p => p.RecordPaymentAsync(InvoiceId, 100m, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRecordingResult.NotFound());

        var result = await Controller(ClientId).Record(InvoiceId, Request(100m), default);

        AssertForbidden(result);
    }

    // ---------------------------------- refusals the invoice's state causes ----

    [Theory]
    [InlineData(PaymentRecordingOutcome.ExceedsOutstanding, InvoiceStatus.Pending, "ExceedsOutstanding")]
    [InlineData(PaymentRecordingOutcome.AlreadyPaid, InvoiceStatus.Paid, "AlreadyPaid")]
    public async Task A_payment_the_invoice_cannot_take_is_a_409_naming_the_reason(
        PaymentRecordingOutcome outcome,
        InvoiceStatus statusNow,
        string expectedReason)
    {
        // Overpaying a Pending invoice and paying a Paid one are the two invalid
        // moves the repository can report.
        StubInvoiceOwnedByCaller();
        _payments
            .Setup(p => p.RecordPaymentAsync(InvoiceId, 1_500m, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRecordingResult.Rejected(outcome, 600m, statusNow));

        var result = await Controller(ClientId).Record(InvoiceId, Request(1_500m), default);

        var conflict = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);

        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(expectedReason, problem.Extensions["reason"]);
        Assert.Equal(600m, problem.Extensions["outstandingAmount"]);
    }

    // -------------------------------------------------------------- helpers ----

    /// <summary>The stubs every owned-invoice test starts from.</summary>
    private void StubInvoiceOwnedByCaller()
    {
        _invoices
            .Setup(i => i.GetAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceOnProject());
        _owners
            .Setup(o => o.IsOwnedByAsync(ProjectId, ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private ClientPaymentsController Controller(Guid? callerId) =>
        new(_payments.Object, _invoices.Object, _owners.Object)
        {
            ControllerContext = CallerContext.For(callerId, PlatformRoles.Client)
        };

    private static RecordPaymentRequest Request(decimal amount) => new() { Amount = amount };

    private static Invoice InvoiceOnProject() => new()
    {
        Id = InvoiceId,
        ProjectId = ProjectId,
        Amount = 1_000m,
        Status = InvoiceStatus.Pending,
        CreatedBy = Guid.NewGuid(),
        CreatedAtUtc = new DateTime(2026, 8, 31, 14, 5, 0, DateTimeKind.Utc)
    };

    private static Payment PaymentOf(decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = InvoiceId,
        Amount = amount,
        PaidBy = ClientId,
        RecordedAtUtc = new DateTime(2026, 8, 31, 14, 6, 0, DateTimeKind.Utc)
    };

    private static void AssertForbidden(IActionResult result)
    {
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ProblemDetails>(problem.Value).Status);
    }
}
