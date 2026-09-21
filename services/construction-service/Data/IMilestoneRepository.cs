using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// Signalled by the repository when a <c>CreateAsync</c> attempt lands on a
/// <c>(project_id, name)</c> pair a milestone already occupies. Kept as a
/// distinct type so the controller can translate it to <c>409 Conflict</c>
/// without matching on a raw <c>MySqlException</c>.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> deliberately does not include the project
/// id. The controller passes it straight through as the problem-details
/// <c>detail</c>, and the frontend renders that detail verbatim next to the
/// PM's "Add a milestone" form on the project's own page — where the
/// project id is context the PM already has. A raw Guid in the sentence
/// reads as machine noise; naming only the milestone keeps the message
/// about what the PM just did. The <see cref="ProjectId"/> and
/// <see cref="Name"/> properties still carry the values for anywhere they
/// are needed programmatically (logging, structured telemetry).
/// </remarks>
public sealed class DuplicateMilestoneNameException : Exception
{
    public DuplicateMilestoneNameException(Guid projectId, string name)
        : base($"A milestone named '{name}' already exists on this project.")
    {
        ProjectId = projectId;
        Name = name;
    }

    public Guid ProjectId { get; }

    public string Name { get; }
}

/// <summary>Data access for <c>construction_milestones</c>.</summary>
public interface IMilestoneRepository
{
    /// <summary>
    /// Creates a milestone for a project — only if the project has already
    /// had its design approved.
    /// </summary>
    /// <remarks>
    /// The design-approval gate (US-12 AC-1) is answered locally: a
    /// <c>milestone_setups</c> row exists for a project exactly when its
    /// <c>DesignApproved</c> event has been consumed (US-23), so the check
    /// is a <c>SELECT</c> against that table in the same connection as the
    /// <c>INSERT</c> — no cross-service call. Returns the created milestone,
    /// or <c>null</c> when the project has no <c>milestone_setups</c> row
    /// (the caller maps that to <c>400 Bad Request</c>).
    /// <para>
    /// Throws <see cref="DuplicateMilestoneNameException"/> when the
    /// <c>(project_id, name)</c> pair is already taken.
    /// </para>
    /// </remarks>
    Task<Milestone?> CreateAsync(
        Guid projectId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a milestone to a new status and stamps <c>updated_at</c>.
    /// </summary>
    /// <remarks>
    /// Returns the updated milestone, or <c>null</c> when no row has that id
    /// (the caller maps that to <c>404 Not Found</c>). The three-state
    /// domain is enforced by <see cref="MilestoneStatus"/> before the call
    /// reaches the database.
    /// </remarks>
    Task<Milestone?> UpdateStatusAsync(
        Guid milestoneId,
        MilestoneStatus newStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates any of the given template names that a project does not
    /// already have. Idempotent — names already on the project are left
    /// alone, so a PM clicking twice or applying the template after typing
    /// one or two by hand does not hit a duplicate-name conflict.
    /// </summary>
    /// <remarks>
    /// The same design-approval gate as <see cref="CreateAsync"/> applies —
    /// a project without a <c>milestone_setups</c> row (the marker the
    /// <c>DesignApproved</c> consumer plants) gets <c>null</c> back, and the
    /// caller maps that to <c>400 Bad Request</c>. Every insert runs in one
    /// transaction alongside the gate check, so a race that revokes the
    /// gate mid-flight rolls back the whole batch — the caller never sees
    /// a half-applied template.
    /// <para>
    /// Returns the milestones the project has after this call for the
    /// given template names — the union of "was already there" and "just
    /// inserted", oldest first. An empty result means the gate refused;
    /// callers distinguish an empty template application ("nothing new to
    /// insert, but everything asked for is already there") by the whole
    /// list still coming back rather than <c>null</c>.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Milestone>?> CreateFromTemplateAsync(
        Guid projectId,
        IReadOnlyList<string> templateNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every milestone defined for a project, oldest first, so the PM sees
    /// them in the order they were planned.
    /// </summary>
    Task<IReadOnlyList<Milestone>> ListForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The project's aggregated progress, computed on the fly from the
    /// milestones' current statuses (US-12 AC-3).
    /// </summary>
    /// <remarks>
    /// Always returns a value — a project with no milestones yet is
    /// zero-of-zero at zero percent, not a failure. A project with no
    /// <c>milestone_setups</c> row (design not yet approved) returns
    /// <c>null</c>, which the caller maps to <c>404</c>.
    /// </remarks>
    Task<ProjectProgress?> GetProgressForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}