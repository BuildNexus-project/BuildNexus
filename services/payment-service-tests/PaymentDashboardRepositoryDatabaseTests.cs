using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// <see cref="Data.PaymentDashboardRepository"/> against real MySQL — the ownership
/// join, the Pending gate and the paid-so-far sum behind a Client's payments due
/// (US-21 AC-1).
/// </summary>
/// <remarks>
/// A stub cannot answer any of this. The query joins three tables and groups, and
/// whether an invoice with several payments is counted once, whether one with none
/// stays in at zero paid, and whether "outstanding" agrees with what the pay endpoint
/// will accept are questions only the engine can settle.
/// <para>
/// Needs <c>payment-db</c> running — see <see cref="PaymentDatabaseFixture"/>. Every
/// query is narrowed by a Client id no other row carries.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PaymentDatabaseCollection.Name)]
public class PaymentDashboardRepositoryDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    /// <summary>A Client no other test or row shares.</summary>
    private readonly Guid _clientId = Guid.NewGuid();

    public PaymentDashboardRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_client_sees_the_unsettled_invoices_on_their_own_projects()
    {
        var projectId = _fixture.ProjectId("dda01");
        await OwnedByAsync(projectId, _clientId);
        var invoice = await InvoiceOn(projectId, 500m);

        var due = Assert.Single(await DueAsync());

        Assert.Equal(invoice, due.InvoiceId);
        Assert.Equal(projectId, due.ProjectId);
        Assert.Equal(500m, due.Amount);
    }

    [Fact]
    public async Task An_invoice_nobody_has_paid_against_stays_in_with_nothing_paid()
    {
        // The LEFT JOIN case: no payment rows at all, so SUM is NULL and must read as zero.
        var projectId = _fixture.ProjectId("dda02");
        await OwnedByAsync(projectId, _clientId);
        await InvoiceOn(projectId, 500m);

        var due = Assert.Single(await DueAsync());

        Assert.Equal(0m, due.AmountPaid);
        Assert.Equal(500m, due.OutstandingAmount);
    }

    [Fact]
    public async Task A_part_paid_invoice_reports_what_is_paid_and_what_is_left()
    {
        var projectId = _fixture.ProjectId("dda03");
        await OwnedByAsync(projectId, _clientId);
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Pay(invoice, 300m);
        await Pay(invoice, 200m);

        var due = Assert.Single(await DueAsync());

        // Two payments, and the invoice's amount is still counted once — grouping is
        // by invoice, not by payment.
        Assert.Equal(1_000m, due.Amount);
        Assert.Equal(500m, due.AmountPaid);
        Assert.Equal(500m, due.OutstandingAmount);
    }

    [Fact]
    public async Task A_settled_invoice_is_not_due()
    {
        var projectId = _fixture.ProjectId("dda04");
        await OwnedByAsync(projectId, _clientId);
        var settled = await InvoiceOn(projectId, 300m);
        await Pay(settled, 300m);
        var owing = await InvoiceOn(projectId, 700m);

        var due = Assert.Single(await DueAsync());

        Assert.Equal(owing, due.InvoiceId);
    }

    [Fact]
    public async Task A_client_never_sees_an_invoice_on_a_project_they_do_not_own()
    {
        var mine = _fixture.ProjectId("dda05");
        var someoneElses = _fixture.ProjectId("dda06");
        await OwnedByAsync(mine, _clientId);
        await OwnedByAsync(someoneElses, Guid.NewGuid());
        var myInvoice = await InvoiceOn(mine, 400m);
        await InvoiceOn(someoneElses, 900m);

        var due = await DueAsync();

        Assert.Equal([myInvoice], due.Select(d => d.InvoiceId));
    }

    [Fact]
    public async Task An_invoice_on_a_project_with_no_recorded_owner_is_shown_to_nobody()
    {
        // No project_owners row: this service cannot say whose it is, so it says nobody's.
        await InvoiceOn(_fixture.ProjectId("dda07"), 400m);

        Assert.Empty(await DueAsync());
    }

    [Fact]
    public async Task Invoices_across_all_of_a_clients_projects_come_back_oldest_first()
    {
        var first = _fixture.ProjectId("dda08");
        var second = _fixture.ProjectId("dda09");
        await OwnedByAsync(first, _clientId);
        await OwnedByAsync(second, _clientId);

        var oldest = await InvoiceOn(second, 100m);
        var middle = await InvoiceOn(first, 200m);
        var newest = await InvoiceOn(second, 300m);

        var due = await DueAsync();

        Assert.Equal([oldest, middle, newest], due.Select(d => d.InvoiceId));
    }

    [Fact]
    public async Task A_client_who_owes_nothing_gets_an_empty_list()
    {
        Assert.Empty(await DueAsync());
    }

    [Fact]
    public async Task What_is_outstanding_agrees_with_the_billing_view_the_pay_endpoint_relies_on()
    {
        // US-17's history and US-16's payment check are the definitions of "outstanding";
        // a dashboard figure that disagreed would offer an amount the pay endpoint refuses.
        var projectId = _fixture.ProjectId("dda10");
        await OwnedByAsync(projectId, _clientId);
        var invoice = await InvoiceOn(projectId, 1_000m);
        await Pay(invoice, 250m);

        var due = Assert.Single(await DueAsync());
        var history = await _fixture.PaymentHistoryRepository.GetForProjectAsync(projectId);

        Assert.Equal(history.OutstandingBalance, due.OutstandingAmount);
    }

    // ------------------------------------------------------------ helpers ----

    private async Task<IReadOnlyList<PaymentDue>> DueAsync() =>
        await _fixture.PaymentDashboardRepository.ListPaymentsDueForClientAsync(_clientId);

    private Task OwnedByAsync(Guid projectId, Guid clientId) =>
        _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId);

    private async Task<Guid> InvoiceOn(Guid projectId, decimal amount) =>
        (await _fixture.InvoiceRepository.CreateAsync(projectId, amount, Guid.NewGuid())).Id;

    private Task Pay(Guid invoiceId, decimal amount) =>
        _fixture.PaymentRepository.RecordPaymentAsync(invoiceId, amount, Guid.NewGuid());
}
