namespace BuildNexus.ConstructionService.Models;

/// <summary>
/// The canonical construction milestones a "Create from template" action
/// plants on a project (US-12 Definition of Done). Kept as an ordered list
/// so a fresh project's list reads in the natural build order — foundation
/// first, finishing last.
/// </summary>
/// <remarks>
/// A template application is idempotent: names already on the project are
/// left alone, so a PM can click twice or apply the template after typing
/// one or two by hand without hitting a duplicate-name conflict. The order
/// here is the order missing names are inserted in, so their created_at
/// timestamps carry the same natural sequence the list reads.
/// </remarks>
public static class MilestoneTemplates
{
    /// <summary>The seven canonical milestones, in the order they would naturally be executed.</summary>
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        "Foundation",
        "Walls",
        "Roof",
        "Electrical",
        "Plumbing",
        "Painting",
        "Finishing",
    };
}