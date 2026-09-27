using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An in-memory <see cref="IPaymentHistoryRepository"/> for the controller
/// suite.
/// </summary>
/// <remarks>
/// Composes the same way the SQL does — invoices newest first, payments grouped
/// to their own invoice, the balance summed from the per-invoice remainders — so
/// a controller test set up with a partial payment behaves like the real thing.
/// The SQL itself is covered against the real engine in
/// <see cref="PaymentHistoryRepositoryDatabaseTests"/>.
/// </remarks>
public class FakePaymentHistoryRepository : IPaymentHistoryRepository
{
    private readonly List<Invoice> _invoices = [];
    private readonly List<Payment> _payments = [];

    /// <summary>Every project the controller asked about, in order.</summary>
    public List<Guid> Reads { get; } = [];

    /// <summary>Plants an invoice on a project, newest last.</summary>
    public Guid GiveInvoice(Guid projectId, decimal amount, InvoiceStatus status = InvoiceStatus.Pending)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Amount = amount,
            Status = status,
            CreatedBy = Guid.NewGuid(),
            // Nudged forward per row so the newest-first ordering is
            // deterministic rather than depending on how fast the test runs.
            CreatedAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc).AddMinutes(_invoices.Count),
            PaidAtUtc = status == InvoiceStatus.Paid
                ? new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc)
                : null
        };

        _invoices.Add(invoice);

        return invoice.Id;
    }

    /// <summary>Plants a payment against an invoice.</summary>
    public void GivePayment(Guid invoiceId, decimal amount) =>
        _payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            Amount = amount,
            PaidBy = Guid.NewGuid(),
            RecordedAtUtc = new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc).AddMinutes(_payments.Count)
        });

    public Task<ProjectPaymentHistory> GetForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        Reads.Add(projectId);

        var composed = _invoices
            .Where(invoice => invoice.ProjectId == projectId)
            .OrderByDescending(invoice => invoice.CreatedAtUtc)
            .Select(invoice =>
            {
                IReadOnlyList<Payment> payments =
                [
                    .. _payments
                        .Where(payment => payment.InvoiceId == invoice.Id)
                        .OrderBy(payment => payment.RecordedAtUtc)
                ];

                var amountPaid = payments.Sum(payment => payment.Amount);

                return new InvoiceWithPayments
                {
                    Invoice = invoice,
                    Payments = payments,
                    AmountPaid = amountPaid,
                    OutstandingAmount = invoice.Amount - amountPaid
                };
            })
            .ToList();

        return Task.FromResult(new ProjectPaymentHistory
        {
            ProjectId = projectId,
            Invoices = composed,
            TotalInvoiced = composed.Sum(entry => entry.Invoice.Amount),
            TotalPaid = composed.Sum(entry => entry.AmountPaid),
            OutstandingBalance = composed.Sum(entry => entry.OutstandingAmount)
        });
    }
}
