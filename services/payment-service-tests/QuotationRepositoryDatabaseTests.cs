using MySqlConnector;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The <c>quotations</c> SQL against the real engine (US-15, AC-1).
/// </summary>
[Collection(PaymentDatabaseCollection.Name)]
public class QuotationRepositoryDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public QuotationRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_generated_quotation_is_linked_to_its_project_and_keeps_its_estimated_total()
    {
        // AC-1, the whole bullet: linked to the project, stores an estimated total.
        var projectId = _fixture.ProjectId("a01");
        var createdBy = Guid.NewGuid();

        var quotation = await _fixture.QuotationRepository.CreateAsync(projectId, 1_250_000.50m, createdBy);

        Assert.NotEqual(Guid.Empty, quotation.Id);
        Assert.Equal(projectId, quotation.ProjectId);
        Assert.Equal(1_250_000.50m, quotation.EstimatedTotal);
        Assert.Equal(createdBy, quotation.CreatedBy);

        // And it is actually readable back out of the database, not just returned
        // from the object the insert built.
        var stored = Assert.Single(await _fixture.QuotationRepository.ListForProjectAsync(projectId));
        Assert.Equal(quotation.Id, stored.Id);
        Assert.Equal(1_250_000.50m, stored.EstimatedTotal);
    }

    [Fact]
    public async Task An_estimated_total_survives_the_round_trip_exactly()
    {
        // The reason the column is DECIMAL and the property is decimal. A figure
        // like this one is not representable in binary floating point, so a
        // double anywhere on the path would read back as 1249999.9899999999 and
        // this assertion would fail.
        var projectId = _fixture.ProjectId("a02");

        await _fixture.QuotationRepository.CreateAsync(projectId, 1_249_999.99m, Guid.NewGuid());

        var stored = Assert.Single(await _fixture.QuotationRepository.ListForProjectAsync(projectId));

        Assert.Equal(1_249_999.99m, stored.EstimatedTotal);
    }

    [Fact]
    public async Task A_project_can_be_requoted_and_the_newest_estimate_comes_first()
    {
        // Re-quoting does not overwrite: the earlier figure is the record of what
        // the Client was told before, so both rows survive and the current
        // estimate is the one at the front.
        var projectId = _fixture.ProjectId("a03");

        var first = await _fixture.QuotationRepository.CreateAsync(projectId, 900_000m, Guid.NewGuid());
        var second = await _fixture.QuotationRepository.CreateAsync(projectId, 1_100_000m, Guid.NewGuid());

        var quotations = await _fixture.QuotationRepository.ListForProjectAsync(projectId);

        Assert.Equal(2, quotations.Count);
        Assert.Equal(second.Id, quotations[0].Id);
        Assert.Equal(first.Id, quotations[1].Id);
    }

    [Fact]
    public async Task A_quotation_is_only_listed_for_the_project_it_was_raised_against()
    {
        // The read is scoped by project. Without the WHERE, one Client's estimate
        // would appear on another project's quote list.
        var quoted = _fixture.ProjectId("a04");
        var other = _fixture.ProjectId("a05");

        await _fixture.QuotationRepository.CreateAsync(quoted, 500_000m, Guid.NewGuid());

        Assert.Empty(await _fixture.QuotationRepository.ListForProjectAsync(other));
    }

    [Fact]
    public async Task A_project_that_has_never_been_quoted_lists_nothing_rather_than_failing()
    {
        // An unquoted project is a normal state, not an error — the Client's view
        // says "no quotation yet" instead of showing a failure.
        Assert.Empty(await _fixture.QuotationRepository.ListForProjectAsync(_fixture.ProjectId("a06")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task The_database_refuses_a_non_positive_estimated_total(decimal estimatedTotal)
    {
        // The second half of the validation pair. The request validator refuses
        // this first, but the CHECK constraint is what stops a caller that reached
        // the repository directly — which is the only reason it is worth asserting
        // that MySQL, and not just C#, says no.
        var projectId = _fixture.ProjectId("a07");

        await Assert.ThrowsAsync<MySqlException>(
            () => _fixture.QuotationRepository.CreateAsync(projectId, estimatedTotal, Guid.NewGuid()));

        Assert.Empty(await _fixture.QuotationRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task A_stored_timestamp_reads_back_as_UTC()
    {
        // MySqlConnector returns DATETIME as DateTimeKind.Unspecified, which
        // System.Text.Json serializes without a Z — and the browser then reads as
        // local time, dating the quotation hours off. The repository stamps the
        // Kind on read; this is what holds it there.
        var projectId = _fixture.ProjectId("a08");

        await _fixture.QuotationRepository.CreateAsync(projectId, 750_000m, Guid.NewGuid());

        var stored = Assert.Single(await _fixture.QuotationRepository.ListForProjectAsync(projectId));

        Assert.Equal(DateTimeKind.Utc, stored.CreatedAtUtc.Kind);
    }
}
