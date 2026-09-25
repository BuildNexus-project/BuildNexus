using System.Data.Common;
using MySqlConnector;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Opens MySQL connections against the <c>PaymentDb</c> connection string.
/// </summary>
public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PaymentDb")
            ?? throw new InvalidOperationException("Connection string 'PaymentDb' is not configured.");
    }

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
