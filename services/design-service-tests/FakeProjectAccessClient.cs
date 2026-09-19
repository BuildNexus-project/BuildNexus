using BuildNexus.DesignService.Models;
using BuildNexus.DesignService.Projects;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// An <see cref="IProjectAccessClient"/> that answers with whatever the test
/// sets, and records what it was asked — so a test can both drive the branch it
/// cares about and check the caller's token was forwarded.
/// </summary>
public sealed class FakeProjectAccessClient : IProjectAccessClient
{
    /// <summary>The answer to give. Defaults to "allowed, nobody assigned".</summary>
    public ProjectAccess Result { get; set; } = ProjectAccess.Allowed(null);

    /// <summary>The project id of the last call, or <c>null</c> if it was never called.</summary>
    public Guid? LastProjectId { get; private set; }

    /// <summary>The token forwarded on the last call.</summary>
    public string? LastToken { get; private set; }

    public int Calls { get; private set; }

    public Task<ProjectAccess> GetAccessAsync(
        Guid projectId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastProjectId = projectId;
        LastToken = bearerToken;

        return Task.FromResult(Result);
    }
}
