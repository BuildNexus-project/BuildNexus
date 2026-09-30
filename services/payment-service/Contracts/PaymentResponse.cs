using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// One payment as the API returns it (US-16).
/// </summary>
public class PaymentResponse
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>The Client who recorded it.</summary>
    public Guid PaidBy { get; set; }

    public DateTime RecordedAtUtc { get; set; }

    public static PaymentResponse From(Payment payment) => new()
    {
        Id = payment.Id,
        InvoiceId = payment.InvoiceId,
        Amount = payment.Amount,
        PaidBy = payment.PaidBy,
        RecordedAtUtc = payment.RecordedAtUtc
    };
}

/// <summary>
/// What recording a payment answers with: the payment, and what it left behind.
/// </summary>
/// <remarks>
/// The invoice's new state travels back with the payment rather than leaving the
/// screen to re-read it. A Client who has just paid wants to know two things
/// immediately — how much is still owed, and whether the invoice is now settled
/// — and a second round trip to learn them would show a stale balance in between.
/// </remarks>
public class RecordPaymentResponse
{
    public required PaymentResponse Payment { get; set; }

    /// <summary>What is still owed on the invoice after this payment. Zero once settled.</summary>
    public required decimal OutstandingAmount { get; set; }

    /// <summary>
    /// The invoice's status after this payment — <c>Paid</c> here is AC-2 having
    /// fired. Serialised as its name by the converter registered in
    /// <c>Program.cs</c>.
    /// </summary>
    public required InvoiceStatus InvoiceStatus { get; set; }

    public static RecordPaymentResponse From(
        Payment payment,
        decimal outstandingAmount,
        InvoiceStatus invoiceStatus) => new()
    {
        Payment = PaymentResponse.From(payment),
        OutstandingAmount = outstandingAmount,
        InvoiceStatus = invoiceStatus
    };
}
