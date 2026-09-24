using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An in-memory <see cref="IQuotationRepository"/>, so the controller suite can
/// assert what the controller does without a database in the way.
/// </summary>
/// <remarks>
/// The SQL itself is covered against the real engine in
/// <see cref="QuotationRepositoryDatabaseTests"/>. What this stands in for is
/// only the storage, so a controller test fails for a controller reason.
/// </remarks>
public class FakeQuotationRepository : IQuotationRepository
{
    private readonly List<Quotation> _quotations = [];

    /// <summary>Every call the controller made, in order, for asserting on arguments.</summary>
    public List<(Guid ProjectId, decimal EstimatedTotal, Guid CreatedBy)> Created { get; } = [];

    public Task<Quotation> CreateAsync(
        Guid projectId,
        decimal estimatedTotal,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        Created.Add((projectId, estimatedTotal, createdBy));

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EstimatedTotal = estimatedTotal,
            CreatedBy = createdBy,
            // Nudged forward per row so the newest-first ordering below is
            // deterministic rather than depending on how fast the test runs.
            CreatedAtUtc = DateTime.UtcNow.AddMilliseconds(_quotations.Count)
        };

        _quotations.Add(quotation);

        return Task.FromResult(quotation);
    }

    public Task<IReadOnlyList<Quotation>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Quotation> rows =
        [
            .. _quotations
                .Where(quotation => quotation.ProjectId == projectId)
                .OrderByDescending(quotation => quotation.CreatedAtUtc)
        ];

        return Task.FromResult(rows);
    }
}
