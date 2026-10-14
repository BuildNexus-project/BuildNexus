using BuildNexus.DesignService.Models;
using BuildNexus.DesignService.Projects;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// An <see cref="IProjectDirectoryClient"/> that answers with whatever the test
/// sets, and records the token it was asked with — so a test can both drive the
/// branch it cares about and check the caller's token was forwarded.
/// </summary>
public sealed class FakeProjectDirectoryClient : IProjectDirectoryClient
{
    /// <summary>The answer to give. Defaults to "available, no projects".</summary>
    public VisibleProjects Result { get; set; } = VisibleProjects.Available([]);

    /// <summary>The token forwarded on the last call, or <c>null</c> if it was never called.</summary>
    public string? LastToken { get; private set; }

    public int Calls { get; private set; }

    public Task<VisibleProjects> ListVisibleProjectsAsync(string bearerToken, CancellationToken cancellationToken)
    {
        Calls++;
        LastToken = bearerToken;

        return Task.FromResult(Result);
    }
}
