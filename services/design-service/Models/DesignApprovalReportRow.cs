namespace BuildNexus.DesignService.Models;

/// <summary>
/// One project's line in the design approval report (US-20): how much revision
/// its design documents went through, and how long they took to approve.
/// </summary>
/// <remarks>
/// Aggregated across the project's documents, since a project can have several
/// (floor plan, elevations, …), each approved on its own cycle. A project with
/// design documents but nothing approved yet is still a row — a high
/// <see cref="TotalVersionCount"/> with a null approval time is exactly the
/// bottleneck the report is for.
/// </remarks>
public class DesignApprovalReportRow
{
    public required Guid ProjectId { get; init; }

    /// <summary>Design documents the project has, of any status.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>How many of those have reached an approved version.</summary>
    public required int ApprovedDocumentCount { get; init; }

    /// <summary>Every version uploaded across the project's documents.</summary>
    public required int TotalVersionCount { get; init; }

    /// <summary>
    /// Mean version number at which a document was approved — the number of
    /// drafts an approved design took, counting the approved one.
    /// <c>null</c> until at least one document is approved.
    /// </summary>
    public required double? AverageVersionsToApproval { get; init; }

    /// <summary>
    /// Mean hours from a document's first upload to its approval, across the
    /// approved documents. <c>null</c> until at least one is approved.
    /// </summary>
    public required double? AverageHoursToApproval { get; init; }
}
