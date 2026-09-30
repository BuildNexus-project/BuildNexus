using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Data;

/// <summary>Read-only queries behind the role dashboards (US-21).</summary>
/// <remarks>
/// Kept apart from <see cref="IDesignDocumentRepository"/> the same way the report
/// query is: these are aggregate read shapes, and a handler that writes has no
/// business being able to reach them.
/// <para>
/// Both take the project ids to look at rather than deciding them. This service
/// has no record of who owns or is assigned to a project — that fact lives in the
/// Project Service — so the caller asks that service which projects the person may
/// see, and passes them in.
/// </para>
/// </remarks>
public interface IDesignDashboardRepository
{
    /// <summary>
    /// How the design documents of each given project divide by the review state
    /// of their latest version. A project with no documents is absent from the
    /// result — the caller decides how to present it.
    /// </summary>
    Task<IReadOnlyList<ProjectDesignTally>> GetDesignTalliesAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every document in the given projects whose latest version is a revision the
    /// Client asked for, longest-waiting first.
    /// </summary>
    Task<IReadOnlyList<PendingRevision>> ListPendingRevisionsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default);
}
