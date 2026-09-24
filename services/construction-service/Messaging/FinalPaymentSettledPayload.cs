using System.Text.Json.Serialization;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// The fields this service reads from a <c>FinalPaymentSettled</c> event's payload —
/// the project whose final payment landed, and when.
/// </summary>
/// <remarks>
/// !!! CONTRACT NOT YET AGREED !!! The Payment Service is unbuilt, so this shape is
/// US-14's proposal rather than something both sides have signed off. Kept to the two
/// fields the handover gate actually needs, which is also what makes it cheap to
/// reconcile later: whatever else that service decides to send — an invoice id, an
/// amount, a method — is ignored here rather than breaking this consumer.
/// </remarks>
public sealed class FinalPaymentSettledPayload
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; init; }

    [JsonPropertyName("settledAt")]
    public DateTimeOffset SettledAt { get; init; }
}
