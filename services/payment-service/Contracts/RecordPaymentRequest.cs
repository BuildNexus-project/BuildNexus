using System.ComponentModel.DataAnnotations;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// What a Client submits to record a payment against an invoice (US-16).
/// </summary>
/// <remarks>
/// The invoice comes from the route and the payer from the caller's own
/// <c>sub</c> claim — neither is accepted from the body, so a payment cannot be
/// attributed to somebody else or moved to another invoice.
/// <para>
/// The cap AC-1 describes is deliberately <em>not</em> here: the outstanding
/// amount is a fact about the invoice that only the database knows, and a bound
/// in this contract could only ever be the invoice's total, which is the wrong
/// number the moment a partial payment exists. The range below is the column's,
/// not the rule's.
/// </para>
/// </remarks>
public class RecordPaymentRequest
{
    /// <summary>
    /// What is being paid. Must be positive, and no larger than the column
    /// holds — refusing an over-large figure here turns what MySQL would answer
    /// as a 500 into a 400 that says what was wrong.
    /// </summary>
    [Required(ErrorMessage = "A payment amount is required.")]
    [Range(typeof(decimal), "0.01", "9999999999999.99",
        ErrorMessage = "The amount must be greater than zero and no more than 9999999999999.99.")]
    public decimal Amount { get; set; }
}
