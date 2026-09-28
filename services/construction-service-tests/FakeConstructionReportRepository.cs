using BuildNexus.ConstructionService.Data;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// An <see cref="IConstructionReportRepository"/> that answers with whatever rows a test
/// asks for, so <see cref="Controllers.ReportsController"/> can be checked without MySQL.
/// </summary>
/// <remarks>
/// The aggregate SQL — the active-project gate, the breakdown counts — is what
/// <see cref="ConstructionReportRepositoryDatabaseTests"/> exercises against the real
/// engine. What this stands in for is the controller's own mapping and its empty-result
/// behaviour.
/// </remarks>
public sealed class FakeConstructionReportRepository : IConstructionReportRepository
{
    /// <summary>How many times the report was asked for.</summary>
    public int CallCount { get; private set; }

    /// <summary>What the repository answers. Empty by default — the no-active-projects case.</summary>
    public IReadOnlyList<ConstructionProgressReportRow> Rows { get; set; } = [];

    public Task<IReadOnlyList<ConstructionProgressReportRow>> GetProgressAcrossActiveProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Rows);
    }
}
