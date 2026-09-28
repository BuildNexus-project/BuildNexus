namespace BuildNexus.ProjectService.Users;

/// <summary>
/// Turns the account ids a project stores into the names people know them by.
/// </summary>
/// <remarks>
/// The accounts live in the User Service's own database and this service holds
/// no copy of them, so a name is asked for at the moment a project is read
/// rather than stored beside the id — it can never be stale, and a project's
/// history written before this existed reads the same as one written today.
/// <para>
/// Unlike <see cref="IUserDirectoryClient"/> this does not forward the caller's
/// token. Every role that may view a project — a Client included — has to see
/// who worked on it, and only an Admin may read <c>GET /api/users/{id}</c>, so
/// the lookup goes over the internal service-to-service endpoint instead.
/// </para>
/// </remarks>
public interface IUserNameResolver
{
    /// <summary>
    /// Looks up each distinct id in <paramref name="userIds"/>.
    /// </summary>
    /// <returns>
    /// The full name of every account that could be resolved. An id with no
    /// entry is one the User Service does not know, or could not be asked about
    /// — never an error: a page that cannot show a name still shows the project,
    /// so the caller falls back to what it already had.
    /// </returns>
    Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken);
}
