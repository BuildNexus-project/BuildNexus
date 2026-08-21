using System.Data.Common;
using BuildNexus.UserService.Models;
using MySqlConnector;

namespace BuildNexus.UserService.Data;

/// <summary>
/// ADO.NET data access for the <c>users</c> table. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class UserRepository : IUserRepository
{
    private const string SelectColumns =
        "id, full_name, email, password_hash, role, is_active, created_at, updated_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public UserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM users WHERE email = @email);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@email", email);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) == 1;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        const string sql = $"SELECT {SelectColumns} FROM users WHERE email = @email LIMIT 1;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@email", email);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapUser(reader) : null;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        const string sql = $"SELECT {SelectColumns} FROM users WHERE id = @id LIMIT 1;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapUser(reader) : null;
    }

    public async Task InsertAsync(User user)
    {
        const string sql = @"
            INSERT INTO users
                (id, full_name, email, password_hash, role, is_active, created_at, updated_at)
            VALUES
                (@id, @fullName, @email, @passwordHash, @role, @isActive, @createdAt, @updatedAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", user.Id);
        AddParameter(command, "@fullName", user.FullName);
        AddParameter(command, "@email", user.Email);
        AddParameter(command, "@passwordHash", user.PasswordHash);
        AddParameter(command, "@role", user.Role.ToString());
        AddParameter(command, "@isActive", user.IsActive);
        AddParameter(command, "@createdAt", user.CreatedAt);
        AddParameter(command, "@updatedAt", user.UpdatedAt);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // The unique index on email is the final word on duplicates, even if two
            // registrations for the same address arrive at the same moment.
            throw new DuplicateEmailException(user.Email);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static User MapUser(DbDataReader reader) => new()
    {
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        FullName = reader.GetString(reader.GetOrdinal("full_name")),
        Email = reader.GetString(reader.GetOrdinal("email")),
        PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
        Role = Enum.Parse<UserRole>(reader.GetString(reader.GetOrdinal("role"))),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
