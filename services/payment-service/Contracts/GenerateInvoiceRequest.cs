using System.ComponentModel.DataAnnotations;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// What a Project Manager or Admin submits to raise an invoice against a project
/// (US-15, AC-2).
/// </summary>
/// <remarks>
/// The project id comes from the route and the author from the caller's own
/// <c>sub</c> claim. There is no status field: a raised invoice is always
/// <c>Pending</c>, and letting a caller declare one already paid would make the
/// billing record a matter of who asked.
/// </remarks>
public class GenerateInvoiceRequest
{
    /// <summary>
    /// What is being billed.
    /// </summary>
    /// <remarks>
    /// The upper bound is the column's: <c>DECIMAL(15,2)</c> holds thirteen
    /// digits before the decimal point. Refusing an over-large figure here turns
    /// what MySQL would answer as a 500 into a 400 that says what was wrong.
    /// </remarks>
    [Required(ErrorMessage = "An invoice amount is required.")]
    [Range(typeof(decimal), "0.01", "9999999999999.99",
        ErrorMessage = "The amount must be greater than zero and no more than 9999999999999.99.")]
    public decimal Amount { get; set; }
}
