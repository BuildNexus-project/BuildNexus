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
/// Note what is <em>not</em> here: the Construction Service's US-14 handover gate
/// waits on a <c>FinalPaymentSettled</c> on this same topic, and nothing publishes
/// it. That is a real gap, but it is not one US-16 names, and adding an event type
/// no story agreed to is how a topic becomes a contract nobody can reason about.
/// It needs its own story.
/// </para>
/// </remarks>
public static class PaymentEventTypes
{
    /// <summary>A Client has recorded a payment against an invoice (AC-3).</summary>
    public const string PaymentReceived = nameof(PaymentReceived);

    /// <summary>
    /// Every type this service publishes, for code that has to enumerate them —
    /// and for the test that holds the database's own CHECK constraint to the
    /// same set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [PaymentReceived];
}
