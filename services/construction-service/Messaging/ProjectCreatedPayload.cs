using System.Text.Json.Serialization;

namespace BuildNexus.ConstructionService.Messaging;

/// <summary>
/// The fields this service reads from a <c>ProjectCreated</c> event's payload —
/// the project, and the Client who submitted it.
/// </summary>
/// <remarks>
/// A much narrower view of <c>project-service</c>'s own
/// <c>ProjectCreatedPayload</c>: that carries the whole project snapshot — name,
/// location, budget, room counts, status — none of which construction progress
/// needs. Reading only these two keeps the coupling to the two fields US-13
/// actually depends on, so a change to any other field on that payload cannot
/// break this consumer.
/// </remarks>
public sealed class ProjectCreatedPayload
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; init; }

    /// <summary>The Client who submitted the project, as the User Service knows them.</summary>
    [JsonPropertyName("clientId")]
    public Guid ClientId { get; init; }
}
