using System.Data.Common;
using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Data;

/// <summary>
/// ADO.NET data access for the <c>password_reset_tokens</c> table. Direct SQL
/// only — no ORM. Every value reaches MySQL as a bound parameter.
/// </summary>
public class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private const string SelectColumns =
        "id, user_id, token_hash, expires_at, consumed_at, created_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public PasswordResetTokenRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(PasswordResetToken token)
    {
        const string sql = @"
            INSERT INTO password_reset_tokens
                (id, user_id, token_hash, expires_at, consumed_at, created_at)
            VALUES
                (@id, @userId, @tokenHash, @expiresAt, @consumedAt, @createdAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", token.Id);
        AddParameter(command, "@userId", token.UserId);
        AddParameter(command, "@tokenHash", token.TokenHash);
        AddParameter(command, "@expiresAt", token.ExpiresAtUtc);
        AddParameter(command, "@consumedAt", token.ConsumedAtUtc);
        AddParameter(command, "@createdAt", token.CreatedAtUtc);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash)
    {
        const string sql =
            $"SELECT {SelectColumns} FROM password_reset_tokens WHERE token_hash = @tokenHash LIMIT 1;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@tokenHash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapToken(reader) : null;
    }

    public async Task<bool> MarkConsumedAsync(Guid id, DateTime consumedAtUtc)
    {
        // "IS NULL" is what makes a link single-use, and it is enforced here
        // rather than by reading the row first: two requests carrying the same
        // link can both pass a read, but only one can win this UPDATE.
        const string sql = @"
            UPDATE password_reset_tokens
            SET consumed_at = @consumedAt
            WHERE id = @id AND consumed_at IS NULL;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@consumedAt", consumedAtUtc);
        AddParameter(command, "@id", id);

        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> InvalidateOutstandingForUserAsync(Guid userId, DateTime consumedAtUtc)
    {
        // Expired rows are swept up as well. They are already unusable, and
        // leaving them outstanding would only make the table harder to read.
        const string sql = @"
            UPDATE password_reset_tokens
            SET consumed_at = @consumedAt
            WHERE user_id = @userId AND consumed_at IS NULL;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@consumedAt", consumedAtUtc);
        AddParameter(command, "@userId", userId);

        return await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // An outstanding link has a real NULL in consumed_at, not a sentinel date.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static PasswordResetToken MapToken(DbDataReader reader)
    {
        var consumedAt = reader.GetOrdinal("consumed_at");

        return new PasswordResetToken
        {
            // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            TokenHash = reader.GetString(reader.GetOrdinal("token_hash")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at")),
            ConsumedAtUtc = reader.IsDBNull(consumedAt) ? null : reader.GetDateTime(consumedAt),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at"))
        };
    }
}
