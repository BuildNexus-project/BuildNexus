using System.Data.Common;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// Hands out open connections to the Project Service database.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync();
}
