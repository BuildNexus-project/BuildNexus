using System.Data.Common;
using MySqlConnector;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// Opens MySQL connections against the <c>ProjectDb</c> connection string.
/// </summary>
public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ProjectDb")
            ?? throw new InvalidOperationException("Connection string 'ProjectDb' is not configured.");
    }

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
