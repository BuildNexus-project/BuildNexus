namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The <c>project_owners</c> SQL against the real engine (US-15).
/// </summary>
/// <remarks>
/// The idempotency here is enforced by a primary key rather than by a check in
/// C#, so only the real engine can show that a redelivered <c>ProjectCreated</c>
/// is absorbed instead of raising a duplicate-key error.
/// </remarks>
[Collection(PaymentDatabaseCollection.Name)]
public class ProjectOwnerRepositoryDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public ProjectOwnerRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task An_owner_is_recorded_and_answers_the_pair_it_was_recorded_for()
    {
        var projectId = _fixture.ProjectId("b01");
        var clientId = Guid.NewGuid();

        Assert.True(await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId));
        Assert.True(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, clientId));
    }

    [Fact]
    public async Task A_redelivered_event_is_absorbed_by_the_primary_key()
    {
        // INSERT IGNORE against the real key: the second call writes nothing and
        // reports so, rather than throwing a duplicate-key error that would wedge
        // the consumer's partition.
        var projectId = _fixture.ProjectId("b02");
        var clientId = Guid.NewGuid();

        Assert.True(await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId));
        Assert.False(await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, clientId));
    }

    [Fact]
    public async Task The_first_owner_learned_of_wins()
    {
        // It does not overwrite. The Project Service never reassigns a project to
        // a different Client, so a second, different value is bad data rather
        // than an update — and silently taking it would move a project between
        // clients.
        var projectId = _fixture.ProjectId("b03");
        var owner = Guid.NewGuid();
        var impostor = Guid.NewGuid();

        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, owner);
        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, impostor);

        Assert.True(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, owner));
        Assert.False(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, impostor));
    }

    [Fact]
    public async Task Another_clients_project_is_not_owned_by_the_caller()
    {
        var projectId = _fixture.ProjectId("b04");

        await _fixture.ProjectOwnerRepository.RecordOwnerIfAbsentAsync(projectId, Guid.NewGuid());

        Assert.False(await _fixture.ProjectOwnerRepository.IsOwnedByAsync(projectId, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_project_with_no_recorded_owner_is_owned_by_nobody()
    {
        // The state before the ProjectCreated event has been consumed. It denies,
        // which is the safe direction.
        Assert.False(
            await _fixture.ProjectOwnerRepository.IsOwnedByAsync(_fixture.ProjectId("b05"), Guid.NewGuid()));
    }
}
