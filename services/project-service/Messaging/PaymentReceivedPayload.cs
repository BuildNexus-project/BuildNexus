namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// What a <c>PaymentReceived</c> carries, as far as this service cares (US-24).
/// </summary>
/// <remarks>
/// A deliberate subset — the Payment Service also sends the payment id, the amount and who paid,
/// and none of them change what this service does. Reading only the two fields that matter means
/// a field added on the publishing side cannot break this consumer.
/// <para>
/// <see cref="InvoiceStatus"/> is <em>one invoice's</em> status, not the project's. That is why
/// the status this service stores is named for the invoice too — see
/// <see cref="Models.ProjectPaymentStatus.InvoicePaid"/>.
/// </para>
/// </remarks>
public sealed class PaymentReceivedPayload
{
    public Guid ProjectId { get; init; }

    /// <summary>
    /// The status of the invoice the payment was against — <c>Pending</c> if it is still part
    /// paid, <c>Paid</c> once that payment settled it.
    /// </summary>
    public string InvoiceStatus { get; init; } = string.Empty;
}
