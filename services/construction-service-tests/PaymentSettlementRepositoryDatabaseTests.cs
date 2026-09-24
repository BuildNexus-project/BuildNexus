namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.PaymentSettlementRepository"/> against real MySQL — the
/// <c>INSERT IGNORE</c> that absorbs a redelivered <c>FinalPaymentSettled</c>, and the
/// <c>EXISTS</c> that decides whether a project may be handed over (US-14 AC-4).
/// </summary>
/// <remarks>
/// This is the check standing between a finished project and being handed over unpaid,
/// so a stub is not enough: only the real engine can show that the primary key actually
/// refuses a second row, and that an unknown project reads as "not settled" rather than
/// as an error the gate might mistake for a pass.
/// <para>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</para>
/// </remarks>
[Collection(ConstructionDatabaseCollection.Name)]
public class PaymentSettlementRepositoryDatabaseTests
{
    private readonly ConstructionDatabaseFixture _fixture;

    public PaymentSettlementRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Records_a_settlement_and_reads_it_back()
    {
        var projectId = _fixture.ProjectId("f01");
        var settledAt = new DateTime(2026, 9, 18, 14, 5, 0, DateTimeKind.Utc);

        var recorded = await _fixture.PaymentSettlementRepository.RecordSettlementIfAbsentAsync(
            projectId, Guid.NewGuid(), settledAt);

        Assert.True(recorded);
        Assert.True(await _fixture.PaymentSettlementRepository.IsSettledAsync(projectId));
    }

    [Fact]
    public async Task A_project_with_no_settlement_reads_as_not_settled()
    {
        // The gate's default, and the safe direction: a project held back can be handed
        // over once the marker arrives, whereas one handed over unpaid cannot be
        // un-handed.
        var projectId = _fixture.ProjectId("f02");

        Assert.False(await _fixture.PaymentSettlementRepository.IsSettledAsync(projectId));
    }

    [Fact]
    public async Task A_second_event_for_the_same_project_records_nothing()
    {
        // Kafka delivers at least once: a redelivered FinalPaymentSettled must be
        // absorbed by the primary key rather than raising a duplicate-key error that
        // would wedge the consumer's partition.
        var projectId = _fixture.ProjectId("f03");
        var settledAt = new DateTime(2026, 9, 18, 14, 5, 0, DateTimeKind.Utc);

        var first = await _fixture.PaymentSettlementRepository.RecordSettlementIfAbsentAsync(
            projectId, Guid.NewGuid(), settledAt);
        var second = await _fixture.PaymentSettlementRepository.RecordSettlementIfAbsentAsync(
            projectId, Guid.NewGuid(), settledAt);

        Assert.True(first);
        Assert.False(second);
        Assert.True(await _fixture.PaymentSettlementRepository.IsSettledAsync(projectId));
    }

    [Fact]
    public async Task One_projects_settlement_does_not_settle_another()
    {
        // The query answers the exact project rather than "any settlement exists" — a
        // mistake there would hand over every finished project the moment any one of
        // them was paid for.
        var paid = _fixture.ProjectId("f04");
        var unpaid = _fixture.ProjectId("f05");

        await _fixture.PlantSettledPaymentAsync(paid);

        Assert.True(await _fixture.PaymentSettlementRepository.IsSettledAsync(paid));
        Assert.False(await _fixture.PaymentSettlementRepository.IsSettledAsync(unpaid));
    }
}
