namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.MilestoneSetupRepository"/> against real MySQL — the
/// <c>INSERT IGNORE</c> that gives US-23 its idempotency, and the round trip of
/// every column through <c>milestone_setups</c>.
/// </summary>
/// <remarks>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</remarks>
[Collection(ConstructionDatabaseCollection.Name)]
public class MilestoneSetupRepositoryDatabaseTests
{
    private static readonly DateTime ApprovedAt = new(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc);

    private readonly ConstructionDatabaseFixture _fixture;

    public MilestoneSetupRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Creates_a_placeholder_and_reads_every_column_back()
    {
        var projectId = _fixture.ProjectId("a01");
        var documentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var created = await _fixture.Repository.CreatePlaceholderIfAbsentAsync(
            projectId, documentId, eventId, ApprovedAt);

        Assert.True(created);

        var row = Assert.Single(
            await _fixture.Repository.ListAsync(),
            setup => setup.ProjectId == projectId);

        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(documentId, row.SourceDocumentId);
        Assert.Equal(eventId, row.SourceEventId);
        Assert.Equal(ApprovedAt, row.ApprovedAtUtc);
        // created_at is stamped by the repository, not the caller.
        Assert.NotEqual(default, row.CreatedAtUtc);
    }

    [Fact]
    public async Task A_second_call_for_the_same_project_creates_nothing()
    {
        // US-23: a project's second approved document, or a redelivered
        // DesignApproved, must not add a row and is not an error.
        var projectId = _fixture.ProjectId("a02");

        var first = await _fixture.Repository.CreatePlaceholderIfAbsentAsync(
            projectId, Guid.NewGuid(), Guid.NewGuid(), ApprovedAt);
        var second = await _fixture.Repository.CreatePlaceholderIfAbsentAsync(
            projectId, Guid.NewGuid(), Guid.NewGuid(), ApprovedAt.AddHours(2));

        Assert.True(first);
        Assert.False(second);

        var rows = (await _fixture.Repository.ListAsync())
            .Where(setup => setup.ProjectId == projectId)
            .ToList();

        Assert.Single(rows);
    }
}
