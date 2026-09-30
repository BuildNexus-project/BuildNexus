using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An in-memory <see cref="IPaymentRepository"/> for the controller suite.
/// </summary>
/// <remarks>
/// Models the cap and the Paid transition the same way the SQL does — invoice
/// total minus the payments so far — so a controller test that sets up a partial
/// payment behaves like the real thing. The transaction and the row lock are
/// what it cannot stand in for, and those are covered against the real engine in
/// <see cref="PaymentRepositoryDatabaseTests"/>.
/// </remarks>
public class FakePaymentRepository : IPaymentRepository
{
    private readonly Dictionary<Guid, decimal> _invoiceTotals = [];
    private readonly List<Payment> _payments = [];

    /// <summary>Every attempt the controller made, in order, refused ones included.</summary>
    public List<(Guid InvoiceId, decimal Amount, Guid PaidBy)> Attempts { get; } = [];

    /// <summary>Registers an invoice of <paramref name="total"/> for payments to be made against.</summary>
    public void GiveInvoice(Guid invoiceId, decimal total) => _invoiceTotals[invoiceId] = total;

    public Task<PaymentRecordingResult> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        Guid paidBy,
        CancellationToken cancellationToken = default)
    {
        Attempts.Add((invoiceId, amount, paidBy));

        if (!_invoiceTotals.TryGetValue(invoiceId, out var total))
        {
            return Task.FromResult(PaymentRecordingResult.NotFound());
        }

        var outstanding = total - _payments.Where(p => p.InvoiceId == invoiceId).Sum(p => p.Amount);

        if (outstanding <= 0m)
        {
            return Task.FromResult(
                PaymentRecordingResult.Rejected(PaymentRecordingOutcome.AlreadyPaid, 0m, InvoiceStatus.Paid));
        }

        if (amount > outstanding)
        {
            return Task.FromResult(PaymentRecordingResult.Rejected(
                PaymentRecordingOutcome.ExceedsOutstanding, outstanding, InvoiceStatus.Pending));
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            Amount = amount,
            PaidBy = paidBy,
            RecordedAtUtc = DateTime.UtcNow
        };

        _payments.Add(payment);

        var remaining = outstanding - amount;

        return Task.FromResult(PaymentRecordingResult.Recorded(
            payment, remaining, remaining == 0m ? InvoiceStatus.Paid : InvoiceStatus.Pending));
    }

    public Task<IReadOnlyList<Payment>> ListForInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Payment> rows =
            [.. _payments.Where(p => p.InvoiceId == invoiceId).OrderBy(p => p.RecordedAtUtc)];

        return Task.FromResult(rows);
    }
}
