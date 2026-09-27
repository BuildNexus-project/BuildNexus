using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// One invoice with everything paid against it, as the API returns it
/// (US-17, AC-1).
/// </summary>
public class InvoiceWithPaymentsResponse
{
    /// <summary>
    /// The invoice itself. Reuses US-15's contract rather than restating its
    /// fields, so the shape a Client already reads on their cost view is the
    /// same one they read here.
    /// </summary>
    public required InvoiceResponse Invoice { get; set; }

    /// <summary>The payments made against it, oldest first — the order they happened.</summary>
    public required IReadOnlyList<PaymentResponse> Payments { get; set; }

    /// <summary>What has been paid against this invoice so far.</summary>
    public required decimal AmountPaid { get; set; }

    /// <summary>
    /// What is still owed on this invoice. Zero on a settled one, and exactly
    /// the figure US-16's pay endpoint will accept — the two are the same
    /// arithmetic on purpose.
    /// </summary>
    public required decimal OutstandingAmount { get; set; }

    public static InvoiceWithPaymentsResponse From(InvoiceWithPayments entry) => new()
    {
        Invoice = InvoiceResponse.From(entry.Invoice),
        Payments = [.. entry.Payments.Select(PaymentResponse.From)],
        AmountPaid = entry.AmountPaid,
        OutstandingAmount = entry.OutstandingAmount
    };
}

/// <summary>
/// A project's billing picture in one answer: the history and the balance
/// (US-17).
/// </summary>
/// <remarks>
/// Both halves travel together because AC-2 requires the balance on the same
/// view as the history. Two endpoints would let a payment land between them and
/// show a Client a balance that does not match the invoices beneath it.
/// </remarks>
public class PaymentHistoryResponse
{
    public required Guid ProjectId { get; set; }

    /// <summary>The project's invoices, most recent first (AC-1).</summary>
    public required IReadOnlyList<InvoiceWithPaymentsResponse> Invoices { get; set; }

    /// <summary>Everything ever billed on this project, settled or not.</summary>
    public required decimal TotalInvoiced { get; set; }

    /// <summary>Everything ever paid on this project.</summary>
    public required decimal TotalPaid { get; set; }

    /// <summary>AC-2's outstanding balance: what is still owed across the project.</summary>
    public required decimal OutstandingBalance { get; set; }

    public static PaymentHistoryResponse From(ProjectPaymentHistory history) => new()
    {
        ProjectId = history.ProjectId,
        Invoices = [.. history.Invoices.Select(InvoiceWithPaymentsResponse.From)],
        TotalInvoiced = history.TotalInvoiced,
        TotalPaid = history.TotalPaid,
        OutstandingBalance = history.OutstandingBalance
    };
}
