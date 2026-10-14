using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The Admin dashboard's user count (US-21), run against real MySQL: grouping by
/// role and by active state is SQL, so the SQL is what has to be shown to work.
/// </summary>
/// <remarks>
/// Needs the development database running — see <see cref="UserServiceFactory"/>.
/// The database also holds the seeded Admin and whatever else a developer has
/// registered, so the system-wide tally cannot be asserted as an absolute number;
/// each test reads it before and after creating its own accounts and asserts the
/// difference. It shares the host collection so no other class is registering
/// accounts while those two reads are taken.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(UserServiceCollection.Name)]
public class UserDashboardRepositoryDatabaseTests : IAsyncLifetime
{
    private static readonly DateTime Moment = new(2031, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Unique to this test — xUnit builds the class once per test method — and
    /// starting with the factory's prefix, so its cleanup would find these
    /// accounts too if this one did not.
    /// </summary>
    private readonly string _tag = $"{UserServiceFactory.TestEmailPrefix}dashboard-{Guid.NewGuid():N}";

    private UserRepository _users = null!;
    private UserDashboardRepository _dashboard = null!;

    public Task InitializeAsync()
    {
        // The production migration path, so this is safe against a database that
        // is empty, a story behind or already current.
        DatabaseMigrator.Migrate(UserServiceFactory.ConnectionString, NullLogger.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserDb"] = UserServiceFactory.ConnectionString
            })
            .Build();

        var connectionFactory = new MySqlConnectionFactory(configuration);

        _users = new UserRepository(connectionFactory);
        _dashboard = new UserDashboardRepository(connectionFactory);

        return Task.CompletedTask;
    }

    /// <summary>Removes the accounts this test created and nothing else.</summary>
    public async Task DisposeAsync()
    {
        await using var connection = new MySqlConnection(UserServiceFactory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM users WHERE email LIKE @prefix;";
        command.Parameters.AddWithValue("@prefix", _tag + "%");

        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Counts_each_role_and_active_state_separately()
    {
        var before = await TallyAsync();

        await RegisterAsync(UserRole.Client, active: true);
        await RegisterAsync(UserRole.Client, active: true);
        await RegisterAsync(UserRole.Client, active: false);
        await RegisterAsync(UserRole.Architect, active: true);

        var after = await TallyAsync();

        Assert.Equal(2, After(after, before, UserRole.Client, active: true));
        Assert.Equal(1, After(after, before, UserRole.Client, active: false));
        Assert.Equal(1, After(after, before, UserRole.Architect, active: true));
        Assert.Equal(0, After(after, before, UserRole.Architect, active: false));
        Assert.Equal(0, After(after, before, UserRole.ProjectManager, active: true));
    }

    [Fact]
    public async Task A_deactivated_account_is_still_counted_as_an_account()
    {
        var before = await TallyAsync();

        await RegisterAsync(UserRole.ProjectManager, active: false);

        var after = await TallyAsync();

        Assert.Equal(1, after.Values.Sum() - before.Values.Sum());
    }

    [Fact]
    public async Task Every_role_the_platform_has_can_be_counted()
    {
        // The CHECK constraint on users.role and the UserRole enum have to agree,
        // or the query would throw on a role it cannot parse.
        var before = await TallyAsync();

        foreach (var role in Enum.GetValues<UserRole>())
        {
            await RegisterAsync(role, active: true);
        }

        var after = await TallyAsync();

        foreach (var role in Enum.GetValues<UserRole>())
        {
            Assert.Equal(1, After(after, before, role, active: true));
        }
    }

    // ------------------------------------------------------------ helpers ----

    private async Task<Dictionary<(UserRole Role, bool IsActive), int>> TallyAsync() =>
        (await _dashboard.CountByRoleAndStatusAsync())
            .ToDictionary(group => (group.Role, group.IsActive), group => group.Count);

    /// <summary>How much a role/state pair grew by, counting a pair the query left out as zero.</summary>
    private static int After(
        Dictionary<(UserRole Role, bool IsActive), int> after,
        Dictionary<(UserRole Role, bool IsActive), int> before,
        UserRole role,
        bool active) =>
        after.GetValueOrDefault((role, active)) - before.GetValueOrDefault((role, active));

    private Task RegisterAsync(UserRole role, bool active) =>
        _users.InsertAsync(new User
        {
            Id = Guid.NewGuid(),
            FullName = "Dashboard Test",
            Email = $"{_tag}-{Guid.NewGuid():N}@example.com",
            // Never read: nothing here signs in.
            PasswordHash = "not-a-real-hash",
            Role = role,
            IsActive = active,
            CreatedAt = Moment,
            UpdatedAt = Moment
        });
}
