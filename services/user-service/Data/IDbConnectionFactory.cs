using System.Data.Common;

namespace BuildNexus.UserService.Data;

/// <summary>
/// Hands out open connections to the User Service database.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync();
}
