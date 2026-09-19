namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The placeholder a <c>DesignApproved</c> event creates: a marker that a
/// project's construction prep should be set up (US-23). One per project.
/// </summary>
public class MilestoneSetup
{
    public required Guid Id { get; init; }

    /// <summary>The project whose design was approved. Unique — one placeholder per project.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>The design document whose approval triggered this — kept for tracing, not unique.</summary>
    public required Guid SourceDocumentId { get; init; }

    /// <summary>The <c>eventId</c> of the <c>DesignApproved</c> envelope that created it.</summary>
    public required Guid SourceEventId { get; init; }

    /// <summary>When the design was approved, from the event's <c>occurredAt</c>.</summary>
    public required DateTime ApprovedAtUtc { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
