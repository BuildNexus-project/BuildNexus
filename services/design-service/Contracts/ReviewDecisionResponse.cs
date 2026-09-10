namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// What a review action (US-11) recorded: the version, the decision, who made
/// it, when, and — for a revision request — what they asked for.
/// </summary>
public class ReviewDecisionResponse
{
    public Guid VersionId { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>The <c>GroundFloorPlan_v2</c> label, same as <see cref="DesignVersionResponse"/>.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary><c>Approved</c> or <c>RevisionRequested</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid ReviewedBy { get; set; }

    public DateTime ReviewedAt { get; set; }

    /// <summary>The Client's note on what needs to change. <c>null</c> for an approval.</summary>
    public string? ReviewComment { get; set; }
}
