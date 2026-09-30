namespace BuildNexus.PaymentService.Models;

/// <summary>
/// One cost estimate a Project Manager or Admin drew up for a project (US-15,
/// AC-1). Owned entirely by the Payment Service — no other service sees or
/// joins against these rows.
/// </summary>
/// <remarks>
/// A project may have more than one. Re-quoting is normal as scope firms up, and
/// overwriting the previous figure would destroy the record of what the Client
/// was originally told, so each generation is its own row and the newest is the
/// project's current estimate.
/// </remarks>
public class Quotation
{
    public required Guid Id { get; init; }

    /// <summary>
    /// The project this estimate is for. Not a foreign key across a service
    /// boundary — the project id is copied off the request and stored as a plain
    /// column, the same way every other service records one.
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// What the build is expected to cost. <see cref="decimal"/> rather than
    /// <see cref="double"/> all the way through, matching the column's
    /// <c>DECIMAL(15,2)</c>: money routed through binary floating point drifts on
    /// comparison and summation.
    /// </summary>
    public required decimal EstimatedTotal { get; init; }

    /// <summary>
    /// The Project Manager or Admin who generated it, from the <c>sub</c> claim
    /// of their token. A cost estimate is a statement someone made to a Client,
    /// and one with no author cannot be questioned later.
    /// </summary>
    public required Guid CreatedBy { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
