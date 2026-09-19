using System.Data.Common;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Hands out open connections to the Construction Service database.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync();
}
