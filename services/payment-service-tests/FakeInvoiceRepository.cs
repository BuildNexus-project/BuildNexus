using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An in-memory <see cref="IInvoiceRepository"/> for the controller suites.
/// </summary>
/// <remarks>
/// The SQL is covered against the real engine in
/// <see cref="InvoiceRepositoryDatabaseTests"/>; this stands in only for the
/// storage, so a controller test fails for a controller reason.
/// </remarks>
public class FakeInvoiceRepository : IInvoiceRepository
{
    private readonly List<Invoice> _invoices = [];

    /// <summary>Every call a controller made, in order, for asserting on arguments.</summary>
    public List<(Guid ProjectId, decimal Amount, Guid CreatedBy)> Created { get; } = [];

    public Task<Invoice> CreateAsync(
        Guid projectId,
        decimal amount,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        Created.Add((projectId, amount, createdBy));

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Amount = amount,
            Status = InvoiceStatus.Pending,
            CreatedBy = createdBy,
            // Nudged forward per row so newest-first ordering is deterministic
            // rather than depending on how fast the test runs.
            CreatedAtUtc = DateTime.UtcNow.AddMilliseconds(_invoices.Count),
            PaidAtUtc = null
        };

        _invoices.Add(invoice);

        return Task.FromResult(invoice);
    }

    /// <summary>The source event ids this fake has already billed, mirroring the unique index.</summary>
    private readonly HashSet<Guid> _billedEvents = [];

    /// <summary>Set to make the next event-sourced write throw, standing in for a database that is down.</summary>
    public Exception? NextWriteThrows { get; set; }

    public Task<Invoice?> CreateFromEventIfAbsentAsync(
        Guid projectId,
        decimal amount,
        Guid raisedBy,
        Guid sourceEventId,
        CancellationToken cancellationToken = default)
    {
        if (NextWriteThrows is not null)
        {
            var toThrow = NextWriteThrows;
            NextWriteThrows = null;
            throw toThrow;
        }

        // Mirrors INSERT IGNORE against uq_invoices_source_event: an event that
        // has already billed is absorbed rather than billing again.
        if (!_billedEvents.Add(sourceEventId))
        {
            return Task.FromResult<Invoice?>(null);
        }

        Created.Add((projectId, amount, raisedBy));

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Amount = amount,
            Status = InvoiceStatus.Pending,
            CreatedBy = raisedBy,
            SourceEventId = sourceEventId,
            CreatedAtUtc = DateTime.UtcNow.AddMilliseconds(_invoices.Count),
            PaidAtUtc = null
        };

        _invoices.Add(invoice);

        return Task.FromResult<Invoice?>(invoice);
    }

    public Task<IReadOnlyList<Invoice>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Invoice> rows =
        [
            .. _invoices
                .Where(invoice => invoice.ProjectId == projectId)
                .OrderByDescending(invoice => invoice.CreatedAtUtc)
        ];

        return Task.FromResult(rows);
    }
}
