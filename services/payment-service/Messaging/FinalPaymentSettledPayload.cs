namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// What a <c>FinalPaymentSettled</c> event carries: the project that owes
/// nothing further, and when it stopped owing.
/// </summary>
/// <remarks>
/// The two fields the Construction Service's handover gate reads, and no more —
/// matching the shape US-14 proposed when it built the consumer, so nothing on
/// that side has to change for this to work.
/// <para>
/// <strong>When this is raised, and why not sooner.</strong> A project's
/// outstanding balance reaching zero is not the same thing as its final payment
/// having landed. A Client who fully pays the invoice raised when their build
/// starts owes nothing at that moment, and will owe more as later stages are
/// billed. Announcing then would let a project be handed over while most of it
/// was still unbilled — a worse failure than the one this fixes, since not
/// handing over unpaid is the entire point of the gate.
/// </para>
/// <para>
/// So the event waits for two facts, and is raised by whichever arrives last:
/// the build is complete, so no further invoices are expected; and the balance
/// is zero, so nothing is owed on what was billed. A project that was never
/// billed owes nothing and qualifies on the second — deliberately, because the
/// alternative would block handover forever on any project nobody invoiced.
/// </para>
/// </remarks>
public sealed class FinalPaymentSettledPayload
{
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// When the project stopped owing anything — the moment the later of the two
    /// facts landed, not when this service got round to sending it.
    /// </summary>
    public required DateTimeOffset SettledAt { get; init; }
}
