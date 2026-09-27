namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// <see cref="Data.ProjectOwnerRepository"/> against real MySQL — the
/// <c>INSERT IGNORE</c> that absorbs a redelivered <c>ProjectCreated</c>, and
/// the <c>EXISTS</c> that decides whether a Client may read a project's
/// progress (US-13).
/// </summary>
/// <remarks>
/// This is the check standing between one Client and another's build progress,
/// so a stub is not enough: only the real engine can show that the primary key
/// actually refuses a second row, and that the query answers the exact
/// (project, client) pair rather than the project alone.
/// <para>Needs <c>construction-db</c> running — see <see cref="ConstructionDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ConstructionDatabaseCollection.Name)]
public class ProjectOwnerRepositoryDatabaseTests
{
    private readonly ConstructionDatabaseFixture _fixture;

    public ProjectOwnerRepositoryDatabaseTests(ConstructionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Records_an_owner_and_reads_the_pair_back()
    {
        var projectId = _fixture.ProjectId("c01");
        var clientId = Guid.NewGuid();

        var recorded = await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId);

        Assert.True(recorded);
        Assert.True(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, clientId));
    }

    [Fact]
    public async Task A_second_call_for_the_same_project_records_nothing()
    {
        // Kafka delivers at least once: a redelivered ProjectCreated must be
        // absorbed by the primary key rather than raising a duplicate-key error
        // that would wedge the consumer's partition.
        var projectId = _fixture.ProjectId("c02");
        var clientId = Guid.NewGuid();

        var first = await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId);
        var second = await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId);

        Assert.True(first);
        Assert.False(second);
        Assert.True(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, clientId));
    }

    [Fact]
    public async Task The_first_owner_learned_for_a_project_wins()
    {
        // INSERT IGNORE does not overwrite. The Project Service never reassigns
        // a project to a different Client, so a differing second value is a
        // replay or a bad message — and quietly rewriting ownership from one
        // would hand the project to the wrong person.
        var projectId = _fixture.ProjectId("c03");
        var realOwner = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, realOwner);
        var overwritten = await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, someoneElse);

        Assert.False(overwritten);
        Assert.True(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, realOwner));
        Assert.False(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, someoneElse));
    }

    [Fact]
    public async Task A_different_client_does_not_own_the_project()
    {
        // The whole point of the check: a signed-in Client guessing a project
        // id must not get past it.
        var projectId = _fixture.ProjectId("c04");

        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, Guid.NewGuid());

        Assert.False(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_project_with_no_recorded_owner_is_owned_by_nobody()
    {
        // The "this service has not learned who owns it yet" case — a project
        // whose ProjectCreated was published before this service started
        // consuming project-events. It denies, rather than defaulting open.
        var projectId = _fixture.ProjectId("c05");

        Assert.False(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, Guid.NewGuid()));
    }
}
