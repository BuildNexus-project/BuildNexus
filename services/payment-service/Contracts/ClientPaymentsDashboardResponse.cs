using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// The payments half of a Client's dashboard (US-21 AC-1): what they still owe, and
/// on which invoices.
/// </summary>
/// <remarks>
/// Only the half this service owns. The projects' names and status, design status and
/// build progress arrive from the Project, Design and Construction services' own
/// dashboard endpoints; the page joins the four on the project id.
/// </remarks>
public class ClientPaymentsDashboardResponse
{
    /// <summary>
    /// What the Client owes across every unsettled invoice — the sum of each invoice's
    /// <see cref="InvoiceDueResponse.OutstandingAmount"/>. Zero when nothing is owing.
    /// </summary>
    public decimal TotalDue { get; set; }

    /// <summary>How many unsettled invoices there are — the length of <see cref="Invoices"/>.</summary>
    public int InvoiceCount { get; set; }

    /// <summary>Oldest first. Empty is a real answer for a Client who owes nothing.</summary>
    public IReadOnlyList<InvoiceDueResponse> Invoices { get; set; } = [];

    public class InvoiceDueResponse
    {
        public Guid InvoiceId { get; set; }

        public Guid ProjectId { get; set; }

        /// <summary>The invoice's full amount.</summary>
        public decimal Amount { get; set; }

        /// <summary>What has been paid against it so far.</summary>
        public decimal AmountPaid { get; set; }

        /// <summary>What is still to pay on it — the figure the pay endpoint will accept up to.</summary>
        public decimal OutstandingAmount { get; set; }

        /// <summary>When the invoice was raised.</summary>
        public DateTime RaisedAt { get; set; }

        public static InvoiceDueResponse From(PaymentDue due) => new()
        {
            InvoiceId = due.InvoiceId,
            ProjectId = due.ProjectId,
            Amount = due.Amount,
            AmountPaid = due.AmountPaid,
            OutstandingAmount = due.OutstandingAmount,
            RaisedAt = due.RaisedAtUtc
        };
    }

    public static ClientPaymentsDashboardResponse From(IReadOnlyList<PaymentDue> due) => new()
    {
        TotalDue = due.Sum(entry => entry.OutstandingAmount),
        InvoiceCount = due.Count,
        Invoices = due.Select(InvoiceDueResponse.From).ToList()
    };
}
