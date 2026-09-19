using System.Data.Common;
using MySqlConnector;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Opens MySQL connections against the <c>ConstructionDb</c> connection string.
/// </summary>
public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ConstructionDb")
            ?? throw new InvalidOperationException("Connection string 'ConstructionDb' is not configured.");
    }

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
