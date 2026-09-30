using System.ComponentModel.DataAnnotations;

namespace BuildNexus.PaymentService.Contracts;

/// <summary>
/// What a Project Manager or Admin submits to generate a cost quotation for a
/// project (US-15, AC-1).
/// </summary>
/// <remarks>
/// The project id comes from the route, and the author comes from the caller's
/// own <c>sub</c> claim — neither is accepted from the body, so a caller cannot
/// attribute an estimate to somebody else.
/// </remarks>
public class GenerateQuotationRequest
{
    /// <summary>
    /// What the build is expected to cost.
    /// </summary>
    /// <remarks>
    /// <see cref="decimal"/> rather than <see cref="double"/> so the figure the
    /// caller sent is the figure that is stored — model binding through a binary
    /// float would round it before validation ever saw it.
    /// <para>
    /// The upper bound is the column's: <c>DECIMAL(15,2)</c> holds thirteen
    /// digits before the decimal point. Refusing an over-large figure here turns
    /// what would otherwise be a 500 from MySQL into a 400 that says what was
    /// wrong.
    /// </para>
    /// </remarks>
    [Required(ErrorMessage = "An estimated total is required.")]
    [Range(typeof(decimal), "0.01", "9999999999999.99",
        ErrorMessage = "The estimated total must be greater than zero and no more than 9999999999999.99.")]
    public decimal EstimatedTotal { get; set; }
}
