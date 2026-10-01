using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Models;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// An <see cref="IPaymentDashboardRepository"/> that answers with whatever a test
/// asks for and remembers whose dashboard it was asked for, so
/// <see cref="Controllers.DashboardController"/> can be checked without MySQL.
/// </summary>
/// <remarks>
/// It does not scope by Client or by invoice status — that is SQL, and
/// <see cref="PaymentDashboardRepositoryDatabaseTests"/> covers it against the real
/// engine. What the controller tests need to know is which Client the endpoint took
/// from the token, and what it did with the rows that came back.
/// </remarks>
public sealed class FakePaymentDashboardRepository : IPaymentDashboardRepository
{
    /// <summary>What the repository answers. Empty by default — the owes-nothing case.</summary>
    public IReadOnlyList<PaymentDue> Due { get; set; } = [];

    /// <summary>The Client the last query was for, or <c>null</c> if none has been made.</summary>
    public Guid? LastClientId { get; private set; }

    /// <summary>How many times the query has run.</summary>
    public int Queries { get; private set; }

    public Task<IReadOnlyList<PaymentDue>> ListPaymentsDueForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        Queries++;
        LastClientId = clientId;

        return Task.FromResult(Due);
    }
}
