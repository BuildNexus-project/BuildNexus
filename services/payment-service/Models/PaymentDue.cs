namespace BuildNexus.PaymentService.Models;

/// <summary>
/// One invoice a Client still owes money on — a line on their dashboard's payments
/// due (US-21 AC-1).
/// </summary>
/// <remarks>
/// A <see cref="InvoiceStatus.Pending"/> invoice, with what has been paid against it
/// so far. The outstanding amount is the same definition US-16 uses when it accepts
/// a payment and US-17 uses on the billing view — the invoice's amount minus the sum
/// of its payments — so a figure shown here is one the pay endpoint will accept.
/// </remarks>
public class PaymentDue
{
    public required Guid InvoiceId { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The invoice's full amount.</summary>
    public required decimal Amount { get; init; }

    /// <summary>What the Client has already paid against it. Zero when nothing has been.</summary>
    public required decimal AmountPaid { get; init; }

    /// <summary>What is still to pay: <see cref="Amount"/> minus <see cref="AmountPaid"/>.</summary>
    public decimal OutstandingAmount => Amount - AmountPaid;

    /// <summary>When the invoice was raised — how long the Client has been billed for it.</summary>
    public required DateTime RaisedAtUtc { get; init; }
}
