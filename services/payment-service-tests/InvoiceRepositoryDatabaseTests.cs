using BuildNexus.PaymentService.Models;
using MySqlConnector;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The <c>invoices</c> SQL against the real engine (US-15, AC-2).
/// </summary>
[Collection(PaymentDatabaseCollection.Name)]
public class InvoiceRepositoryDatabaseTests
{
    private readonly PaymentDatabaseFixture _fixture;

    public InvoiceRepositoryDatabaseTests(PaymentDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_raised_invoice_has_a_unique_id_an_amount_and_a_pending_status()
    {
        // AC-2, the whole bullet.
        var projectId = _fixture.ProjectId("c01");

        var invoice = await _fixture.InvoiceRepository.CreateAsync(projectId, 250_000.75m, Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, invoice.Id);
        Assert.Equal(250_000.75m, invoice.Amount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Null(invoice.PaidAtUtc);

        var stored = Assert.Single(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));
        Assert.Equal(invoice.Id, stored.Id);
        Assert.Equal(InvoiceStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task Every_invoice_gets_its_own_id()
    {
        // AC-2's "unique ID", across two invoices raised against one project in
        // the same moment.
        var projectId = _fixture.ProjectId("c02");

        var first = await _fixture.InvoiceRepository.CreateAsync(projectId, 100m, Guid.NewGuid());
        var second = await _fixture.InvoiceRepository.CreateAsync(projectId, 200m, Guid.NewGuid());

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, (await _fixture.InvoiceRepository.ListForProjectAsync(projectId)).Count);
    }

    [Fact]
    public async Task An_amount_survives_the_round_trip_exactly()
    {
        // Not representable in binary floating point — a double anywhere on the
        // path would read back as 99999.98999999999.
        var projectId = _fixture.ProjectId("c03");

        await _fixture.InvoiceRepository.CreateAsync(projectId, 99_999.99m, Guid.NewGuid());

        var stored = Assert.Single(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));

        Assert.Equal(99_999.99m, stored.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task The_database_refuses_a_non_positive_amount(decimal amount)
    {
        var projectId = _fixture.ProjectId("c04");

        await Assert.ThrowsAsync<MySqlException>(
            () => _fixture.InvoiceRepository.CreateAsync(projectId, amount, Guid.NewGuid()));

        Assert.Empty(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task The_database_refuses_a_status_outside_the_two_the_story_names()
    {
        // The CHECK is the database half of the InvoiceStatus enum. Asserted by
        // going around the repository, since the repository cannot produce a
        // third value — which is the point: the constraint is what stops anything
        // else that reaches this table.
        var projectId = _fixture.ProjectId("c05");

        await using var connection = new MySqlConnection(PaymentDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO invoices (id, project_id, amount, status, created_by, created_at, paid_at)
            VALUES (@id, @projectId, 100.00, 'Cancelled', @createdBy, UTC_TIMESTAMP(6), NULL);";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());
        command.Parameters.AddWithValue("@projectId", projectId);
        command.Parameters.AddWithValue("@createdBy", Guid.NewGuid());

        await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task A_pending_invoice_cannot_carry_a_settlement_date()
    {
        // The two columns must not drift into saying different things: "Pending"
        // with a paid_at would be a row that is both settled and not.
        var projectId = _fixture.ProjectId("c06");

        await using var connection = new MySqlConnection(PaymentDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO invoices (id, project_id, amount, status, created_by, created_at, paid_at)
            VALUES (@id, @projectId, 100.00, 'Pending', @createdBy, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());
        command.Parameters.AddWithValue("@projectId", projectId);
        command.Parameters.AddWithValue("@createdBy", Guid.NewGuid());

        await Assert.ThrowsAsync<MySqlException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Invoices_come_back_newest_first_and_only_for_their_own_project()
    {
        var billed = _fixture.ProjectId("c07");
        var other = _fixture.ProjectId("c08");

        var first = await _fixture.InvoiceRepository.CreateAsync(billed, 100m, Guid.NewGuid());
        var second = await _fixture.InvoiceRepository.CreateAsync(billed, 200m, Guid.NewGuid());

        var invoices = await _fixture.InvoiceRepository.ListForProjectAsync(billed);

        Assert.Equal([second.Id, first.Id], invoices.Select(invoice => invoice.Id));
        Assert.Empty(await _fixture.InvoiceRepository.ListForProjectAsync(other));
    }

    [Fact]
    public async Task An_event_raises_at_most_one_invoice()
    {
        // The guard against billing a client twice, against the real unique index
        // rather than a check in C# that two consumer instances could both pass.
        var projectId = _fixture.ProjectId("c10");
        var eventId = Guid.NewGuid();

        var first = await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 400_000m, Guid.NewGuid(), eventId);
        var second = await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 400_000m, Guid.NewGuid(), eventId);

        Assert.NotNull(first);
        // Null, not a throw: a redelivery must be absorbed, not wedge the
        // consumer's partition on a message it can never get past.
        Assert.Null(second);
        Assert.Single(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task Two_different_events_each_raise_their_own_invoice()
    {
        // The index binds one invoice per causing event, not one per project.
        var projectId = _fixture.ProjectId("c11");

        Assert.NotNull(await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 100m, Guid.NewGuid(), Guid.NewGuid()));
        Assert.NotNull(await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 200m, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(2, (await _fixture.InvoiceRepository.ListForProjectAsync(projectId)).Count);
    }

    [Fact]
    public async Task Manual_invoices_are_not_constrained_by_the_source_event_index()
    {
        // MySQL permits repeated NULLs in a unique index, which is what lets a
        // Project Manager raise as many manual invoices against a project as the
        // job needs while automatic ones stay one-per-event.
        var projectId = _fixture.ProjectId("c12");

        await _fixture.InvoiceRepository.CreateAsync(projectId, 100m, Guid.NewGuid());
        await _fixture.InvoiceRepository.CreateAsync(projectId, 200m, Guid.NewGuid());

        var invoices = await _fixture.InvoiceRepository.ListForProjectAsync(projectId);

        Assert.Equal(2, invoices.Count);
        Assert.All(invoices, invoice => Assert.Null(invoice.SourceEventId));
    }

    [Fact]
    public async Task An_automatic_invoice_remembers_the_event_that_caused_it()
    {
        var projectId = _fixture.ProjectId("c13");
        var eventId = Guid.NewGuid();

        await _fixture.InvoiceRepository.CreateFromEventIfAbsentAsync(
            projectId, 100m, Guid.NewGuid(), eventId);

        var stored = Assert.Single(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));

        Assert.Equal(eventId, stored.SourceEventId);
    }

    [Fact]
    public async Task A_stored_timestamp_reads_back_as_UTC()
    {
        var projectId = _fixture.ProjectId("c09");

        await _fixture.InvoiceRepository.CreateAsync(projectId, 100m, Guid.NewGuid());

        var stored = Assert.Single(await _fixture.InvoiceRepository.ListForProjectAsync(projectId));

        Assert.Equal(DateTimeKind.Utc, stored.CreatedAtUtc.Kind);
    }
}
