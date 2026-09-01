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

    /// <summary>
    /// The same columns minus <c>password_hash</c>. Listings never need it, and
    /// a hash that is never read cannot be leaked by a mapping mistake.
    /// </summary>
    private const string SelectListColumns =
        "id, full_name, email, phone_number, contact_address, role, is_active, created_at, updated_at";

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

    public async Task<PagedResult<User>> ListPageAsync(UserRole? role, int page, int pageSize)
    {
        // One optional filter, applied identically to the page and to the count
        // so the total can never describe a different set than the rows.
        var filter = role is null ? string.Empty : " WHERE role = @role";

        await using var connection = await _connectionFactory.OpenConnectionAsync();

        // Counted first, on the same connection: a caller asking for a page past
        // the end still needs to be told how many rows there really are.
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM users{filter};";

        if (role is not null)
        {
            AddParameter(countCommand, "@role", role.Value.ToString());
        }

        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        await using var pageCommand = connection.CreateCommand();
        // LIMIT and OFFSET are bound like every other value. The controller has
        // already clamped them, but a number pasted into SQL is a number pasted
        // into SQL, and this repository does not do that.
        pageCommand.CommandText =
            $"SELECT {SelectListColumns} FROM users{filter} ORDER BY full_name LIMIT @take OFFSET @skip;";

        if (role is not null)
        {
            AddParameter(pageCommand, "@role", role.Value.ToString());
        }

        AddParameter(pageCommand, "@take", pageSize);
        AddParameter(pageCommand, "@skip", (page - 1) * pageSize);

        return new PagedResult<User>
        {
            Items = await ReadListAsync(pageCommand),
            TotalCount = totalCount
        };
    }

    public async Task<IReadOnlyList<User>> ListActiveByRolesAsync(IReadOnlyCollection<UserRole> roles)
    {
        if (roles.Count == 0)
        {
            // "IN ()" is a syntax error in MySQL, and an empty filter can only
            // ever match nothing — so answer that without a round trip.
            return [];
        }

        // One bound parameter per role rather than the names pasted into the
        // string. They come from our own enum, not from a caller, but the rule
        // in this repository is that no value reaches MySQL any other way.
        var placeholders = string.Join(", ", roles.Select((_, index) => $"@role{index}"));
        var sql = $"SELECT {SelectListColumns} FROM users WHERE is_active = TRUE AND role IN ({placeholders}) ORDER BY full_name;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var ordinal = 0;
        foreach (var role in roles)
        {
            AddParameter(command, $"@role{ordinal}", role.ToString());
            ordinal++;
        }

        return await ReadListAsync(command);
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

    public async Task<bool> UpdateAccountAsync(User user)
    {
        // The administrator's counterpart to UpdateProfileAsync, and just as
        // narrow from the other side: it can move name, email and role, and it
        // lists neither password_hash nor is_active, so no request reaching this
        // layer can change a password or reinstate an account through it.
        const string sql = @"
            UPDATE users
            SET full_name  = @fullName,
                email      = @email,
                role       = @role,
                updated_at = @updatedAt
            WHERE id = @id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@fullName", user.FullName);
        AddParameter(command, "@email", user.Email);
        AddParameter(command, "@role", user.Role.ToString());
        AddParameter(command, "@updatedAt", user.UpdatedAt);
        AddParameter(command, "@id", user.Id);

        try
        {
            // Zero rows means the account was removed between the read and the write.
            return await command.ExecuteNonQueryAsync() > 0;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // The unique index on email is the final word on duplicates here too,
            // even if two administrators move two accounts onto the same address
            // at the same moment.
            throw new DuplicateEmailException(user.Email);
        }
    }

    public async Task<bool> SetActiveAsync(Guid userId, bool isActive, DateTime updatedAtUtc)
    {
        // Narrower still: this statement touches one flag. Deactivating must not
        // be able to disturb a name, an email or a role on its way past.
        const string sql = @"
            UPDATE users
            SET is_active  = @isActive,
                updated_at = @updatedAt
            WHERE id = @id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@isActive", isActive);
        AddParameter(command, "@updatedAt", updatedAtUtc);
        AddParameter(command, "@id", userId);

        // Zero rows means the account was removed between the read and the write.
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, DateTime updatedAtUtc)
    {
        // As narrow as UpdateProfileAsync, from the other end: this statement
        // can only ever move the password hash, never a name, email or role.
        const string sql = @"
            UPDATE users
            SET password_hash = @passwordHash,
                updated_at    = @updatedAt
            WHERE id = @id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@passwordHash", passwordHash);
        AddParameter(command, "@updatedAt", updatedAtUtc);
        AddParameter(command, "@id", userId);

        // Zero rows means the account was removed between the read and the write.
        return await command.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Drains a listing query. The rows carry no <c>password_hash</c>, so
    /// <see cref="User.PasswordHash"/> is left at its empty default.
    /// </summary>
    private static async Task<IReadOnlyList<User>> ReadListAsync(DbCommand command)
    {
        var users = new List<User>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(MapListedUser(reader));
        }

        return users;
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

    private static User MapListedUser(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        FullName = reader.GetString(reader.GetOrdinal("full_name")),
        Email = reader.GetString(reader.GetOrdinal("email")),
        PhoneNumber = GetNullableString(reader, "phone_number"),
        ContactAddress = GetNullableString(reader, "contact_address"),
        Role = Enum.Parse<UserRole>(reader.GetString(reader.GetOrdinal("role"))),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };

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
