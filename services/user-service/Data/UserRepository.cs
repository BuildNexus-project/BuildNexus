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
        "id, full_name, email, phone_number, contact_address, password_hash, role, is_active, created_at, updated_at";

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

    public async Task<bool> AdminExistsAsync()
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM users WHERE role = @role);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@role", nameof(UserRole.Admin));

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) == 1;
    }

    public async Task InsertAsync(User user)
    {
        const string sql = @"
            INSERT INTO users
                (id, full_name, email, phone_number, contact_address, password_hash, role, is_active, created_at, updated_at)
            VALUES
                (@id, @fullName, @email, @phoneNumber, @contactAddress, @passwordHash, @role, @isActive, @createdAt, @updatedAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", user.Id);
        AddParameter(command, "@fullName", user.FullName);
        AddParameter(command, "@email", user.Email);
        AddParameter(command, "@phoneNumber", user.PhoneNumber);
        AddParameter(command, "@contactAddress", user.ContactAddress);
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

    public async Task<bool> UpdateProfileAsync(User user)
    {
        // Deliberately narrow: only the self-editable fields are listed, so no
        // request can reach this layer and move email, role or password_hash.
        const string sql = @"
            UPDATE users
            SET full_name       = @fullName,
                phone_number    = @phoneNumber,
                contact_address = @contactAddress,
                updated_at      = @updatedAt
            WHERE id = @id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@fullName", user.FullName);
        AddParameter(command, "@phoneNumber", user.PhoneNumber);
        AddParameter(command, "@contactAddress", user.ContactAddress);
        AddParameter(command, "@updatedAt", user.UpdatedAt);
        AddParameter(command, "@id", user.Id);

        // Zero rows means the account was removed between the read and the write.
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // An unset contact detail is a real NULL in the column, not an empty string.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static User MapUser(DbDataReader reader) => new()
    {
        // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        FullName = reader.GetString(reader.GetOrdinal("full_name")),
        Email = reader.GetString(reader.GetOrdinal("email")),
        PhoneNumber = GetNullableString(reader, "phone_number"),
        ContactAddress = GetNullableString(reader, "contact_address"),
        PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
        Role = Enum.Parse<UserRole>(reader.GetString(reader.GetOrdinal("role"))),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
