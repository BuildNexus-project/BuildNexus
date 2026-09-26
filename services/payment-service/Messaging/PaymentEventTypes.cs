namespace BuildNexus.PaymentService.Messaging;

/// <summary>
/// The <c>eventType</c> values this service publishes.
/// </summary>
/// <remarks>
/// Only the events US-16 actually names belong here. A consumer subscribes by
/// matching these strings, so an invented one is a contract nobody agreed to and
/// dead weight on the topic — and <c>ck_payment_outbox_events_type</c> holds the
/// database to the same list.
/// <para>
/// <c>FinalPaymentSettled</c> is the contract the Construction Service's US-14
/// handover gate has been waiting on: it consumes that event to build the local
/// replica its gate reads, and until this service published it, handover was
/// refused for every project.
/// </para>
/// </remarks>
public static class PaymentEventTypes
{
    /// <summary>A Client has recorded a payment against an invoice (AC-3).</summary>
    public const string PaymentReceived = nameof(PaymentReceived);

    /// <summary>
    /// A project owes nothing further: its build is complete and every invoice
    /// raised against it is settled.
    /// </summary>
    /// <remarks>
    /// The Construction Service's cue that handover is permitted (US-14 AC-4).
    /// Deliberately <em>not</em> raised the moment a project's balance reaches
    /// zero — see <see cref="FinalPaymentSettledPayload"/> for why a zero balance
    /// on its own does not mean the final payment has landed.
    /// </remarks>
    public const string FinalPaymentSettled = nameof(FinalPaymentSettled);

    /// <summary>
    /// Every type this service publishes, for code that has to enumerate them —
    /// and for the test that holds the database's own CHECK constraint to the
    /// same set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [PaymentReceived, FinalPaymentSettled];
}
