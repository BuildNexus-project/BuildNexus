using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// <see cref="Data.ProjectRepository.UpdatePaymentStatusAsync"/> against real MySQL — the
/// reflected payment status and the deduplication that makes a redelivered
/// <c>PaymentReceived</c> a no-op (US-24 AC-4).
/// </summary>
/// <remarks>
/// A stub cannot answer any of this. The idempotency lives in the statement's own <c>WHERE</c>,
/// using MySQL's NULL-safe <c>&lt;=&gt;</c> because the column starts null — an ordinary
/// <c>&lt;&gt;</c> would answer NULL there and the very first event would never apply. Whether
/// that clause actually matches, and whether the CHECK constraint accepts the three values, are
/// questions only the engine can settle.
/// <para>Needs <c>project-db</c> running — see <see cref="ProjectDatabaseFixture"/>.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ProjectDatabaseCollection.Name)]
public class ProjectPaymentStatusDatabaseTests
{
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");
    private static readonly DateTime PaidAt = new(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc);

    private readonly ProjectDatabaseFixture _fixture;

    public ProjectPaymentStatusDatabaseTests(ProjectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_new_project_starts_NotInvoiced()
    {
        // The default migration 007 gives every row, existing ones included. A project nobody has
        // billed is not "unknown" — it is genuinely not invoiced.
        var project = await CreateProjectAsync();

        var stored = await _fixture.Repository.GetByIdAsync(project.Id);

        Assert.Equal(ProjectPaymentStatus.NotInvoiced, stored!.PaymentStatus);
        Assert.Null(stored.PaymentStatusUpdatedAt);
        Assert.Null(stored.LastPaymentEventId);
    }

    [Theory]
    [InlineData(ProjectPaymentStatus.PartiallyPaid)]
    [InlineData(ProjectPaymentStatus.InvoicePaid)]
    public async Task A_payment_event_moves_the_status_and_records_what_applied_it(
        ProjectPaymentStatus target)
    {
        var project = await CreateProjectAsync();
        var eventId = Guid.NewGuid();

        var applied = await _fixture.Repository.UpdatePaymentStatusAsync(
            project.Id, target, eventId, PaidAt);

        Assert.True(applied);

        var stored = await _fixture.Repository.GetByIdAsync(project.Id);
        Assert.Equal(target, stored!.PaymentStatus);
        // Stamped when the payment happened, not when the message was read.
        Assert.Equal(PaidAt, stored.PaymentStatusUpdatedAt);
        Assert.Equal(eventId, stored.LastPaymentEventId);
    }

    [Fact]
    public async Task The_same_event_delivered_twice_applies_once()
    {
        // Kafka delivers at least once and the Payment Service's dispatcher re-sends anything it
        // could not confirm, so this is the ordinary case rather than the exotic one.
        var project = await CreateProjectAsync();
        var eventId = Guid.NewGuid();

        var first = await _fixture.Repository.UpdatePaymentStatusAsync(
            project.Id, ProjectPaymentStatus.PartiallyPaid, eventId, PaidAt);
        var second = await _fixture.Repository.UpdatePaymentStatusAsync(
            project.Id, ProjectPaymentStatus.PartiallyPaid, eventId, PaidAt.AddHours(3));

        Assert.True(first);
        Assert.False(second);

        // And the later timestamp of the redelivery did not overwrite the real one — which is the
        // damage deduplication prevents, since the status itself would have looked unchanged.
        var stored = await _fixture.Repository.GetByIdAsync(project.Id);
        Assert.Equal(PaidAt, stored!.PaymentStatusUpdatedAt);
    }

    [Fact]
    public async Task A_second_payment_leaving_the_same_status_still_applies()
    {
        // Two different payments can both leave a project PartiallyPaid. That second payment is a
        // real event whose time is worth recording, so it is the event id — not the status — that
        // decides whether something is new.
        var project = await CreateProjectAsync();

        await _fixture.Repository.UpdatePaymentStatusAsync(
            project.Id, ProjectPaymentStatus.PartiallyPaid, Guid.NewGuid(), PaidAt);

        var laterEventId = Guid.NewGuid();
        var applied = await _fixture.Repository.UpdatePaymentStatusAsync(
            project.Id, ProjectPaymentStatus.PartiallyPaid, laterEventId, PaidAt.AddDays(1));

        Assert.True(applied);

        var stored = await _fixture.Repository.GetByIdAsync(project.Id);
        Assert.Equal(PaidAt.AddDays(1), stored!.PaymentStatusUpdatedAt);
        Assert.Equal(laterEventId, stored.LastPaymentEventId);
    }

    [Fact]
    public async Task An_unknown_project_changes_nothing_rather_than_failing()
    {
        // This service owns projects, so an event about one it has never heard of is a bogus or
        // very stale message. False lets the consumer commit past it instead of retrying forever.
        Assert.False(await _fixture.Repository.UpdatePaymentStatusAsync(
            Guid.NewGuid(), ProjectPaymentStatus.InvoicePaid, Guid.NewGuid(), PaidAt));
    }

    [Fact]
    public async Task The_payment_status_survives_a_later_lifecycle_change()
    {
        // The two are independent columns moved by independent events, and neither write may
        // clobber the other's — a project going to Construction must not forget it was paid.
        var project = await CreateProjectAsync();
        var eventId = Guid.NewGuid();

        await _fixture.Repository.UpdatePaymentStatusAsync(
            project.Id, ProjectPaymentStatus.InvoicePaid, eventId, PaidAt);

        var change = new ProjectStatusChange
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            FromStatus = ProjectStatus.Pending,
            ToStatus = ProjectStatus.Designing,
            ChangedByUserId = ClientId,
            ChangedByRole = PlatformRoles.ProjectManager,
            ChangedAt = PaidAt.AddDays(1)
        };

        Assert.True(await _fixture.Repository.UpdateStatusAsync(change, PaidAt.AddDays(1), []));

        var stored = await _fixture.Repository.GetByIdAsync(project.Id);
        Assert.Equal(ProjectStatus.Designing, stored!.Status);
        Assert.Equal(ProjectPaymentStatus.InvoicePaid, stored.PaymentStatus);
        Assert.Equal(eventId, stored.LastPaymentEventId);
    }

    /// <summary>A stored project in this run's namespace, so the fixture's cleanup finds it.</summary>
    private async Task<Project> CreateProjectAsync()
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = ClientId,
            Name = $"{ProjectDatabaseFixture.TestProjectPrefix}{Guid.NewGuid()}",
            Location = "Galle",
            LandSizePerches = 25.5m,
            Budget = 18_500_000m,
            Floors = 2,
            Bedrooms = 4,
            Bathrooms = 3,
            GarageSpaces = 2,
            OtherRequirements = null,
            Status = ProjectStatus.Pending,
            CreatedAt = PaidAt.AddMonths(-1),
            UpdatedAt = PaidAt.AddMonths(-1)
        };

        await _fixture.Repository.InsertAsync(
            project, ProjectStatusChange.ForCreation(project, PlatformRoles.Client), []);

        return project;
    }
}
