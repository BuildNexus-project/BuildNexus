using System.Data.Common;
using MySqlConnector;

namespace BuildNexus.UserService.Data;

/// <summary>
/// Opens MySQL connections against the <c>UserDb</c> connection string.
/// </summary>
public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("UserDb")
            ?? throw new InvalidOperationException("Connection string 'UserDb' is not configured.");
    }

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
