using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// One project's row in <c>GET /api/designs/reports/approval</c> (US-20).
/// </summary>
/// <remarks>
/// A near-mirror of <see cref="DesignApprovalReportRow"/>, kept separate so the
/// wire contract does not move if the model gains a field the report should not
/// expose — the same reason every other response in this service has its own
/// shape. Times are sent as raw hours; the caller decides how to show them.
/// </remarks>
public class DesignApprovalReportRowResponse
{
    public Guid ProjectId { get; set; }

    public int DocumentCount { get; set; }

    public int ApprovedDocumentCount { get; set; }

    public int TotalVersionCount { get; set; }

    /// <summary><c>null</c> until the project has at least one approved document.</summary>
    public double? AverageVersionsToApproval { get; set; }

    /// <summary><c>null</c> until the project has at least one approved document.</summary>
    public double? AverageHoursToApproval { get; set; }

    public static DesignApprovalReportRowResponse From(DesignApprovalReportRow row) => new()
    {
        ProjectId = row.ProjectId,
        DocumentCount = row.DocumentCount,
        ApprovedDocumentCount = row.ApprovedDocumentCount,
        TotalVersionCount = row.TotalVersionCount,
        AverageVersionsToApproval = row.AverageVersionsToApproval,
        AverageHoursToApproval = row.AverageHoursToApproval
    };
}
