using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Data;

/// <summary>
/// ADO.NET read for the Admin dashboard's user counts. Direct SQL only — no ORM.
/// </summary>
public class UserDashboardRepository : IUserDashboardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserDashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<UserCountGroup>> CountByRoleAndStatusAsync(
        CancellationToken cancellationToken = default)
    {
        // Every count on the dashboard is a sum of these lines, so one statement
        // serves them all. `idx_users_role_full_name` leads with role, so the
        // grouping can be served from the index rather than by reading every row.
        const string sql = @"
            SELECT role, is_active, COUNT(*) AS user_count
            FROM users
            GROUP BY role, is_active;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var groups = new List<UserCountGroup>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            groups.Add(new UserCountGroup
            {
                Role = Enum.Parse<UserRole>(reader.GetString(reader.GetOrdinal("role"))),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                // COUNT(*) is a BIGINT; the value is a head count, not a key.
                Count = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("user_count")))
            });
        }

        return groups;
    }
}
