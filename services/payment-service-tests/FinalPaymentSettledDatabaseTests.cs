using BuildNexus.PaymentService.Messaging;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// When a project's final payment is announced — the event US-14's handover gate
/// has been waiting on.
/// </summary>
/// <remarks>
/// The rule spans three tables and a row lock, so these run against the real
/// engine. The case that matters most is the one that must <em>not</em> fire: a
/// project whose balance is momentarily zero but whose build is still running
/// will be billed again, and announcing then would let the Construction Service
/// hand it over half-billed.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PaymentDatabaseCollection.Name)]
public class FinalPaymentSettledDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public FinalPaymentSettledDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ------------------------------------- it must not fire too early ----

    [Fact]
    public async Task A_fully_paid_project_whose_build_is_still_running_is_not_announced()
    {
        // The whole reason completion is a condition. This project owes nothing
        // right now and will owe more as later stages are billed.
        var projectId = _fixture.ProjectId("901");
        await Pay(await InvoiceOn(projectId, 500m), 500m);

        Assert.False(await _fixture.ConstructionCompletionRepository.HasAnnouncedAsync(projectId));
        Assert.Empty(await AnnouncementsFor(projectId));
    }

    [Fact]
    public async Task A_completed_build_that_still_owes_money_is_not_announced()
    {
        var projectId = _fixture.ProjectId("902");
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Pay(invoice, 400m);

        await Complete(projectId);

        Assert.False(await _fixture.ConstructionCompletionRepository.HasAnnouncedAsync(projectId));
        Assert.Empty(await AnnouncementsFor(projectId));
    }

    [Fact]
    public async Task A_partial_payment_on_a_completed_build_does_not_announce()
    {
        var projectId = _fixture.ProjectId("903");
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Complete(projectId);

        await Pay(invoice, 999.99m);

        Assert.Empty(await AnnouncementsFor(projectId));
    }

    // ------------------------------------------ both orderings work ----

    [Fact]
    public async Task The_payment_that_clears_a_completed_project_announces_it()
    {
        // Build finished first, then the money. The payment is the last fact to
        // arrive, so the payment announces.
        var projectId = _fixture.ProjectId("904");
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Complete(projectId);

        await Pay(invoice, 1_000m);

        var announced = Assert.Single(await AnnouncementsFor(projectId));
        Assert.Equal(PaymentEventTypes.FinalPaymentSettled, announced.EventType);
        Assert.True(await _fixture.ConstructionCompletionRepository.HasAnnouncedAsync(projectId));
    }

    [Fact]
    public async Task Completing_the_build_on_an_already_paid_project_announces_it()
    {
        // Money first, then the build finished. Completion is the last fact to
        // arrive, so completion announces — the case that would otherwise leave
        // a finished, fully paid project stuck forever.
        var projectId = _fixture.ProjectId("905");
        await Pay(await InvoiceOn(projectId, 750m), 750m);

        var announced = await Complete(projectId);

        Assert.True(announced);
        Assert.Single(await AnnouncementsFor(projectId));
    }

    [Fact]
    public async Task A_completed_project_that_was_never_billed_is_announced()
    {
        // Nothing was ever owed. Refusing to announce would block handover on
        // this project forever, since a zero invoice cannot be raised.
        var projectId = _fixture.ProjectId("906");

        Assert.True(await Complete(projectId));
        Assert.Single(await AnnouncementsFor(projectId));
    }

    [Fact]
    public async Task The_last_of_several_invoices_being_settled_is_what_announces()
    {
        var projectId = _fixture.ProjectId("907");
        var first = await InvoiceOn(projectId, 400m);
        var second = await InvoiceOn(projectId, 600m);
        await Complete(projectId);

        await Pay(first, 400m);
        Assert.Empty(await AnnouncementsFor(projectId));

        await Pay(second, 600m);
        Assert.Single(await AnnouncementsFor(projectId));
    }

    // ------------------------------------------------- exactly once ----

    [Fact]
    public async Task A_redelivered_completion_does_not_announce_twice()
    {
        // Kafka delivers at least once.
        var projectId = _fixture.ProjectId("908");
        await Pay(await InvoiceOn(projectId, 200m), 200m);

        Assert.True(await Complete(projectId));
        Assert.False(await Complete(projectId));

        Assert.Single(await AnnouncementsFor(projectId));
    }

    [Fact]
    public async Task A_later_invoice_paid_after_settlement_does_not_announce_again()
    {
        // A project can be billed again after being announced. That is a second
        // settlement, not a second final one — the marker holds.
        var projectId = _fixture.ProjectId("909");
        await Pay(await InvoiceOn(projectId, 300m), 300m);
        await Complete(projectId);

        await Pay(await InvoiceOn(projectId, 100m), 100m);

        Assert.Single(await AnnouncementsFor(projectId));
    }

    // ------------------------------------------------ the event itself ----

    [Fact]
    public async Task The_event_carries_the_project_and_when_it_settled()
    {
        // The two fields US-14's consumer reads. Its shape is fixed by that
        // consumer, which this must not break.
        var projectId = _fixture.ProjectId("910");
        await Pay(await InvoiceOn(projectId, 100m), 100m);
        await Complete(projectId);

        var announced = Assert.Single(await AnnouncementsFor(projectId));
        var payload = System.Text.Json.JsonDocument.Parse(announced.Envelope)
            .RootElement.GetProperty("payload");

        Assert.Equal(projectId, payload.GetProperty("projectId").GetGuid());
        Assert.True(payload.TryGetProperty("settledAt", out _));
    }

    [Fact]
    public async Task The_event_names_no_invoice_because_it_is_about_the_project()
    {
        var projectId = _fixture.ProjectId("911");
        await Pay(await InvoiceOn(projectId, 100m), 100m);
        await Complete(projectId);

        Assert.Null(Assert.Single(await AnnouncementsFor(projectId)).InvoiceId);
    }

    [Fact]
    public async Task The_announcement_waits_on_the_outbox_like_any_other_event()
    {
        // So a broker that is down delays handover rather than losing it.
        var projectId = _fixture.ProjectId("912");
        await Pay(await InvoiceOn(projectId, 100m), 100m);
        await Complete(projectId);

        var announced = Assert.Single(await AnnouncementsFor(projectId));

        Assert.False(announced.IsPublished);
        Assert.Contains(
            await _fixture.OutboxRepository.ListPendingAsync(batchSize: 500),
            pending => pending.Id == announced.Id);
    }

    // ---------------------------------------------------------- helpers ----

    private async Task<Guid> InvoiceOn(Guid projectId, decimal amount) =>
        (await _fixture.InvoiceRepository.CreateAsync(projectId, amount, Guid.NewGuid())).Id;

    private Task Pay(Guid invoiceId, decimal amount) =>
        _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, amount, Guid.NewGuid());

    private Task<bool> Complete(Guid projectId) =>
        _fixture.ConstructionCompletionRepository.RecordCompletionAndMaybeAnnounceAsync(
            projectId, DateTime.UtcNow, Guid.NewGuid());

    /// <summary>The FinalPaymentSettled rows raised for a project.</summary>
    private async Task<IReadOnlyList<PaymentService.Models.OutboxEvent>> AnnouncementsFor(Guid projectId) =>
        [.. (await _fixture.OutboxRepository.ListPendingAsync(batchSize: 500))
            .Where(pending =>
                pending.ProjectId == projectId
                && pending.EventType == PaymentEventTypes.FinalPaymentSettled)];
}
