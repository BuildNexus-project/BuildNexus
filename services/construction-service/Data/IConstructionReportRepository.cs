using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Aggregate reads over this service's own milestone data, for the construction half of
/// the combined report (US-19).
/// </summary>
/// <remarks>
/// Separate from <see cref="IMilestoneRepository"/> deliberately: that one answers about
/// one project and is what the per-project screens use, while this one sweeps the whole
/// portfolio for a report. Keeping them apart means the report's queries cannot creep
/// into the request-path repository, and US-12's contract does not move.
/// <para>
/// Reads only. This story writes nothing — it reports on what US-12 and US-14 already
/// store, and no other service's database is touched: the payment half of the report is
/// Payment Service's own endpoint, answered from its own schema.
/// </para>
/// </remarks>
public interface IConstructionReportRepository
{
    /// <summary>
    /// Per-project progress and milestone breakdown across the active projects that have
    /// milestones defined (AC-1), oldest project id first.
    /// </summary>
    /// <remarks>
    /// <b>What "active" means here.</b> This service does not know a project's lifecycle
    /// status — its <c>project-events</c> consumer records ownership from
    /// <c>ProjectCreated</c> and deliberately ignores <c>ProjectUpdated</c>, so there is
    /// no <c>Cancelled</c> or <c>Completed</c> to filter on without replicating that
    /// status first. "Active" is therefore answered from the state this service does
    /// own:
    /// <list type="bullet">
    /// <item>the project has a <c>milestone_setups</c> row — its design is approved, so
    /// a build is planned at all;</item>
    /// <item>and its <c>construction_phases</c> row is not <c>HandedOver</c> — the build
    /// has not been delivered and closed out.</item>
    /// </list>
    /// In project-status terms that is <c>DesignApproved</c> and <c>Construction</c>:
    /// everything earlier has no construction plan to report on, and a handed-over build
    /// is finished.
    /// <para>
    /// The one gap this leaves is a project cancelled while it was
    /// <c>DesignApproved</c> — cancellation is only permitted before the build starts,
    /// so such a project keeps its milestones and would still appear here at 0%. Closing
    /// it means replicating project status off <c>ProjectUpdated</c>, which is a write
    /// this read-only story does not make.
    /// </para>
    /// <para>
    /// A project with no milestones is left out rather than reported at zero: AC-1 is
    /// about projects that "have milestones defined", and a 0% line for a project nobody
    /// has planned yet would read as a stalled build. An empty list is the correct answer
    /// when no active project has any — not an error.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ConstructionProgressReportRow>> GetProgressAcrossActiveProjectsAsync(
        CancellationToken cancellationToken = default);
}
