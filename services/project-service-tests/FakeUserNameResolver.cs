using BuildNexus.ProjectService.Users;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// An <see cref="IUserNameResolver"/> that knows the names a test gives it, and
/// records which ids it was asked about — so a test can both drive the branch it
/// cares about and check the controller asked for the right people.
/// </summary>
/// <remarks>
/// Shared by every suite that builds a <see cref="BuildNexus.ProjectService.Controllers.ProjectsController"/>,
/// for the same reason as <see cref="FakeUserDirectoryClient"/>. It knows nobody
/// by default, which is what the real resolver answers when the User Service
/// cannot be reached — so a suite that says nothing about names is exercising
/// the fallback rather than a case it never asked for.
/// </remarks>
public sealed class FakeUserNameResolver : IUserNameResolver
{
    /// <summary>The names to give back, by account id.</summary>
    public Dictionary<Guid, string> Names { get; } = [];

    /// <summary>The ids of the last call, exactly as passed — duplicates included.</summary>
    public IReadOnlyList<Guid> LastAsked { get; private set; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastAsked = [.. userIds];

        // Only the ids asked about, as the real resolver returns — an id nobody
        // asked for never comes back.
        IReadOnlyDictionary<Guid, string> answer = LastAsked
            .Where(Names.ContainsKey)
            .Distinct()
            .ToDictionary(id => id, id => Names[id]);

        return Task.FromResult(answer);
    }
}
