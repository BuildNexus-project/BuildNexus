namespace BuildNexus.PaymentService.Models;

/// <summary>
/// One payment a Client recorded against an invoice (US-16). Owned entirely by
/// the Payment Service — no other service sees or joins against these rows.
/// </summary>
/// <remarks>
/// Payments are additive and never edited: an invoice's outstanding amount is
/// its total minus the sum of these, so correcting one by changing it in place
/// would rewrite history that the balance is derived from.
/// </remarks>
public class Payment
{
    public required Guid Id { get; init; }

    /// <summary>The invoice this payment pays down.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>
    /// What was paid. <see cref="decimal"/> throughout, matching the column's
    /// <c>DECIMAL(15,2)</c> — these are summed, so a binary float would compound
    /// its drift across every payment on the invoice.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>The Client who recorded it, from the <c>sub</c> claim of their token.</summary>
    public required Guid PaidBy { get; init; }

    public required DateTime RecordedAtUtc { get; init; }
}
