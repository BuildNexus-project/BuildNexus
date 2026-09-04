using System.Data.Common;

namespace BuildNexus.DesignService.Data;

/// <summary>
/// Hands out open connections to the Design Service database.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync();
}
