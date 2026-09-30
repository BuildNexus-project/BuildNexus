using System.Data.Common;

namespace BuildNexus.PaymentService.Data;

/// <summary>
/// Hands out open connections to the Payment Service database.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync();
}
