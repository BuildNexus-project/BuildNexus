using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Data;

/// <summary>Read-only aggregate queries behind the admin reports (US-20).</summary>
/// <remarks>
/// Kept apart from <see cref="IDesignDocumentRepository"/>: that one is the CRUD
/// path a request takes, this one is reporting — different read shapes, and a
/// report query has no business being reachable from an upload handler.
/// </remarks>
public interface IDesignReportRepository
{
    /// <summary>
    /// One row per project that has any design document — its revision volume
    /// and time-to-approval, computed across the project's documents. Ordered
    /// by project id.
    /// </summary>
    Task<IReadOnlyList<DesignApprovalReportRow>> GetApprovalReportAsync(CancellationToken cancellationToken = default);
}
