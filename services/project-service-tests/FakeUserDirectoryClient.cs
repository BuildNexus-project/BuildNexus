using BuildNexus.ProjectService.Models;
using BuildNexus.ProjectService.Users;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// An <see cref="IUserDirectoryClient"/> that answers with whatever the test
/// sets, and records what it was asked — so a test can both drive the branch it
/// cares about and check the Admin's token was forwarded.
/// </summary>
/// <remarks>
/// Shared by every suite that builds a <see cref="BuildNexus.ProjectService.Controllers.ProjectsController"/>,
/// so all of them are provably talking to the same shape rather than each
/// keeping a stub that drifted from the interface. The suites that do not touch
/// staff assignment never call it and take the default.
/// </remarks>
public sealed class FakeUserDirectoryClient : IUserDirectoryClient
{
    /// <summary>The answer to give. Defaults to "an Architect exists", the common case.</summary>
    public UserLookup Result { get; set; } = UserLookup.Found("Architect");

    /// <summary>The account id of the last call, or <c>null</c> if it was never called.</summary>
    public Guid? LastUserId { get; private set; }

    /// <summary>The token forwarded on the last call.</summary>
    public string? LastToken { get; private set; }

    public int Calls { get; private set; }

    public Task<UserLookup> GetUserAsync(Guid userId, string bearerToken, CancellationToken cancellationToken)
    {
        Calls++;
        LastUserId = userId;
        LastToken = bearerToken;

        return Task.FromResult(Result);
    }
}
