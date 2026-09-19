using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// An <see cref="IDesignReportRepository"/> that returns whatever rows it is
/// given, so the controller suite can check the mapping and the gating without
/// MySQL. The aggregate SQL itself is covered by
/// <see cref="DesignReportRepositoryDatabaseTests"/>.
/// </summary>
public sealed class FakeDesignReportRepository : IDesignReportRepository
{
    public IReadOnlyList<DesignApprovalReportRow> Rows { get; set; } = [];

    public Task<IReadOnlyList<DesignApprovalReportRow>> GetApprovalReportAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(Rows);
}
