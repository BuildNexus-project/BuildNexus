namespace BuildNexus.PaymentService.Models;

/// <summary>
/// One invoice with everything paid against it (US-17, AC-1).
/// </summary>
/// <remarks>
/// A read-only composition of rows US-15 and US-16 already write — this story
/// adds no column and stores no total. <see cref="OutstandingAmount"/> is
/// derived on every read for the same reason US-16 derives it inside the
/// payment transaction: a stored figure is one somebody has to keep correct, and
/// a drift there is silent.
/// </remarks>
public class InvoiceWithPayments
{
    public required Invoice Invoice { get; init; }

    /// <summary>The payments made against it, oldest first — the order they happened.</summary>
    public required IReadOnlyList<Payment> Payments { get; init; }

    /// <summary>What has been paid against this invoice so far.</summary>
    public required decimal AmountPaid { get; init; }

    /// <summary>
    /// What is still owed on this invoice: its amount less what has been paid.
    /// Zero on a settled one.
    /// </summary>
    /// <remarks>
    /// The same arithmetic US-16's cap uses —
    /// <c>invoices.amount - SUM(payments.amount)</c> — deliberately, so the
    /// figure this screen shows is the figure the pay endpoint will accept. Two
    /// different definitions of "outstanding" would show a Client a number they
    /// are then refused for entering.
    /// </remarks>
    public required decimal OutstandingAmount { get; init; }
}

/// <summary>
/// A project's whole billing picture: every invoice with its payments, and what
/// is still owed across all of them (US-17).
/// </summary>
public class ProjectPaymentHistory
{
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// The project's invoices, most recent first (AC-1), each with the payments
    /// made against it.
    /// </summary>
    public required IReadOnlyList<InvoiceWithPayments> Invoices { get; init; }

    /// <summary>Everything ever billed on this project, settled or not.</summary>
    public required decimal TotalInvoiced { get; init; }

    /// <summary>Everything ever paid on this project.</summary>
    public required decimal TotalPaid { get; init; }

    /// <summary>
    /// AC-2's outstanding balance: what the Client still owes across the project.
    /// </summary>
    /// <remarks>
    /// Summed from the per-invoice remainders rather than as
    /// <c>TotalInvoiced - TotalPaid</c>. The two agree today — US-16's cap makes
    /// an overpaid invoice impossible, so no remainder can be negative — but the
    /// per-invoice sum stays correct even if one ever were, where the subtraction
    /// would let an overpayment on one invoice quietly cancel out what is still
    /// owed on another.
    /// <para>
    /// AC-2 describes this as the sum over unpaid and partially paid invoices. A
    /// settled invoice contributes zero, so summing every invoice's remainder is
    /// the same figure and needs no special case.
    /// </para>
    /// </remarks>
    public required decimal OutstandingBalance { get; init; }
}
