using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Messaging;

/// <summary>
/// The <c>payload</c> of a <see cref="ProjectEventTypes.ProjectApproved"/>
/// event: whose project was signed off, and who signed it off.
/// </summary>
/// <remarks>
/// The milestone the other services are waiting on. Construction may schedule a
/// build once this arrives, and Payment may raise its first invoice — so this
/// carries the little either needs to act without calling back here, and
/// nothing else.
/// <para>
/// There is no status field. This event says one thing and says it in its type;
/// a consumer that wants the lifecycle is reading
/// <see cref="ProjectEventTypes.ProjectUpdated"/>, which went out immediately
/// before this on the same partition.
/// </para>
/// </remarks>
public sealed class ProjectApprovedPayload
{
    public required Guid ProjectId { get; init; }

    /// <summary>Who to bill and who to tell, as the User Service knows them.</summary>
    public required Guid ClientId { get; init; }

    /// <summary>So a consumer can name the project to a human without calling back here.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The Client's budget for the build.
    /// </summary>
    /// <remarks>
    /// The one piece of the project's description that travels on this event.
    /// Approval is what lets the Payment Service raise its first invoice, and a
    /// figure to invoice against is exactly the context it would otherwise have
    /// to come back here for — which is the coupling the message bus exists to
    /// avoid. <c>decimal</c> throughout, so money does not drift by a rounding
    /// error between services.
    /// </remarks>
    public required decimal Budget { get; init; }

    /// <summary>
    /// Who approved it and the role they held at the time — an Architect, a
    /// Project Manager or an Admin, never the owning Client.
    /// </summary>
    public required Guid ApprovedByUserId { get; init; }

    public required string ApprovedByRole { get; init; }

    /// <summary>
    /// Offset-bearing, so the instant is unambiguous on the wire — a figure a
    /// consumer may put on an invoice should not depend on guessing a zone.
    /// </summary>
    public required DateTimeOffset ApprovedAt { get; init; }

    /// <summary>
    /// Built from the transition that approved it, so the approver and the
    /// moment come from the same record the status history keeps rather than
    /// from a second reading of the clock.
    /// </summary>
    public static ProjectApprovedPayload From(Project project, ProjectStatusChange change) => new()
    {
        ProjectId = project.Id,
        ClientId = project.ClientId,
        Name = project.Name,
        Budget = project.Budget,
        ApprovedByUserId = change.ChangedByUserId,
        ApprovedByRole = change.ChangedByRole,
        ApprovedAt = EventTimestamp.AsUtc(change.ChangedAt)
    };
}
