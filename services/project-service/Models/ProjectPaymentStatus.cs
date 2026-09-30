namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Where a project stands financially, reflected from the Payment Service's
/// <c>PaymentReceived</c> events (US-24).
/// </summary>
/// <remarks>
/// A reflection of somebody else's fact, not this service's own state. The money lives in
/// Payment Service; this exists so a project can be read without asking that service anything,
/// the same way Construction Service holds its own copy of who owns a project.
/// <para>
/// Persisted as the member's own name in <c>projects.payment_status</c>, which is also what
/// the CHECK constraint allows — so a DBA reading the table sees what a row means, and adding
/// a member cannot silently re-label existing rows.
/// </para>
/// </remarks>
public enum ProjectPaymentStatus
{
    /// <summary>
    /// No <c>PaymentReceived</c> has been seen for this project. The default for every project,
    /// including every row that existed before migration 007.
    /// </summary>
    NotInvoiced,

    /// <summary>A payment arrived, and the invoice it was against is still <c>Pending</c>.</summary>
    PartiallyPaid,

    /// <summary>
    /// A payment arrived and settled the invoice it was against.
    /// </summary>
    /// <remarks>
    /// Named for the invoice, not the project, and deliberately so. A <c>PaymentReceived</c>
    /// event carries one invoice's status, and this service cannot see how many other invoices
    /// the project has — nor may it look, since that is Payment Service's schema. So this says
    /// "the invoice that payment settled is paid", never "the project owes nothing". A truthful
    /// project-level answer would have to come from Payment Service as its own published
    /// determination, which is not an event US-24 names.
    /// </remarks>
    InvoicePaid
}
