namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The local record that a project's final payment has been settled — the marker a
/// <c>FinalPaymentSettled</c> event leaves, and AC-4's handover precondition.
/// </summary>
/// <remarks>
/// A replica of one fact the Payment Service owns, kept here so the handover gate is
/// a query against this service's own schema rather than a call across a service
/// boundary. Same shape of arrangement as <see cref="MilestoneSetup"/>, which
/// replicates "the design was approved".
/// <para>
/// There is no "unsettled" row: the absence of a row is what "not yet settled"
/// means. A row exists only once payment has actually landed, so the gate cannot be
/// passed by a row that claims settlement while saying it has not happened.
/// </para>
/// </remarks>
public class PaymentSettlement
{
    /// <summary>
    /// The project whose final payment settled. Not a foreign key across a service
    /// boundary — a plain column copied off an event.
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>The <c>eventId</c> of the envelope that created this row.</summary>
    public required Guid SourceEventId { get; init; }

    /// <summary>When the payment settled, from the event's own payload.</summary>
    public required DateTime SettledAtUtc { get; init; }

    /// <summary>When this service learned the fact.</summary>
    public required DateTime RecordedAtUtc { get; init; }
}
