using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// How an attempt to record a payment ended (US-16).
/// </summary>
public enum PaymentRecordingOutcome
{
    /// <summary>The payment was recorded and the invoice reflects it.</summary>
    Recorded,

    /// <summary>No invoice with that id in this service's schema.</summary>
    InvoiceNotFound,

    /// <summary>
    /// The invoice is already settled, so there is nothing outstanding to pay.
    /// </summary>
    /// <remarks>
    /// Its own answer rather than folded into
    /// <see cref="ExceedsOutstanding"/>: "you have already paid this" and "that
    /// is more than you owe" send a Client to different places, and a screen that
    /// showed the second for a settled invoice would read as a pricing error.
    /// </remarks>
    AlreadyPaid,

    /// <summary>
    /// The amount is larger than the invoice's outstanding amount (AC-1).
    /// </summary>
    ExceedsOutstanding
}

/// <summary>
/// What recording a payment produced, and enough of the invoice's new state for
/// the caller to answer without a second read.
/// </summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Payment">The recorded payment, or <c>null</c> if nothing was recorded.</param>
/// <param name="OutstandingAmount">
/// What is still owed on the invoice. After the payment when one was recorded;
/// the unchanged current figure on every refusal — which is what lets a rejected
/// attempt tell the Client what they may actually pay.
/// </param>
/// <param name="InvoiceStatus">
/// The invoice's status after the attempt, or <c>null</c> when the invoice was
/// not found. <see cref="Models.InvoiceStatus.Paid"/> here is AC-2 having fired.
/// </param>
public sealed record PaymentRecordingResult(
    PaymentRecordingOutcome Outcome,
    Payment? Payment,
    decimal OutstandingAmount,
    InvoiceStatus? InvoiceStatus)
{
    public static PaymentRecordingResult NotFound() =>
        new(PaymentRecordingOutcome.InvoiceNotFound, Payment: null, OutstandingAmount: 0m, InvoiceStatus: null);

    public static PaymentRecordingResult Rejected(
        PaymentRecordingOutcome outcome,
        decimal outstandingAmount,
        InvoiceStatus invoiceStatus) =>
        new(outcome, Payment: null, outstandingAmount, invoiceStatus);

    public static PaymentRecordingResult Recorded(
        Payment payment,
        decimal outstandingAmount,
        InvoiceStatus invoiceStatus) =>
        new(PaymentRecordingOutcome.Recorded, payment, outstandingAmount, invoiceStatus);
}

/// <summary>Data access for <c>payments</c>.</summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Records a payment against an invoice, capped at what is still outstanding
    /// (AC-1), and settles the invoice once the balance reaches zero (AC-2).
    /// </summary>
    /// <remarks>
    /// The whole attempt runs in one transaction, and the invoice row is locked
    /// for the duration. That lock is what actually enforces AC-1: the
    /// outstanding amount is read, checked and written against inside it, so two
    /// payments arriving at once cannot both read the same balance and both be
    /// allowed — which a check in C# over an unlocked read would permit, and
    /// which would overpay the invoice.
    /// <para>
    /// The outstanding amount is derived every time —
    /// <c>invoices.amount - SUM(payments.amount)</c> — never read from a stored
    /// total, so a partial payment followed by a second partial payment is simply
    /// two rows and a smaller remainder, with nothing to keep in step.
    /// </para>
    /// <para>
    /// A payment exactly equal to the outstanding amount is allowed and is what
    /// settles the invoice; only a strictly larger one is refused.
    /// </para>
    /// </remarks>
    /// <param name="paidBy">
    /// The Client recording it, from their token's <c>sub</c> claim. Stored as the
    /// payment's author.
    /// </param>
    Task<PaymentRecordingResult> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        Guid paidBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The payments recorded against an invoice, oldest first — the order they
    /// were made in, which is the order they read as a history.
    /// </summary>
    Task<IReadOnlyList<Payment>> ListForInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);
}
