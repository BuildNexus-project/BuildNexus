using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// One row in <c>GET /api/construction/milestone-setups</c> (US-23): a project
/// whose design was approved and whose construction prep is waiting to be set up.
/// </summary>
/// <remarks>
/// A near-mirror of <see cref="MilestoneSetup"/>, kept separate so the wire
/// contract does not move if the model gains a field this listing should not
/// expose. <c>SourceDocumentId</c> and <c>SourceEventId</c> are carried through
/// for tracing a placeholder back to the event that made it.
/// </remarks>
public class MilestoneSetupResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid SourceDocumentId { get; set; }

    public Guid SourceEventId { get; set; }

    public DateTime ApprovedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public static MilestoneSetupResponse From(MilestoneSetup setup) => new()
    {
        Id = setup.Id,
        ProjectId = setup.ProjectId,
        SourceDocumentId = setup.SourceDocumentId,
        SourceEventId = setup.SourceEventId,
        ApprovedAtUtc = setup.ApprovedAtUtc,
        CreatedAtUtc = setup.CreatedAtUtc
    };
}
