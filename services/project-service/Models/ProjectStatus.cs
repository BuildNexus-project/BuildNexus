namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Where a project sits in its lifecycle. Persisted as its string name in
/// <c>projects.status</c> and in <c>project_status_history</c>.
/// </summary>
/// <remarks>
/// The five values are the lifecycle US-06 defines, in the order a project
/// moves through them. Which moves are actually permitted is
/// <see cref="ProjectStatusTransitions"/>'s business, not this type's — an enum
/// can only say what the states are.
/// <para>
/// Declaration order matches the lifecycle, but nothing depends on the
/// underlying numbers: the name is what is stored, sent and compared, so a
/// value inserted in the middle later would not silently re-label existing
/// rows.
/// </para>
/// </remarks>
public enum ProjectStatus
{
    /// <summary>Submitted by the Client and waiting on the company to pick it up.</summary>
    Pending,

    /// <summary>An Architect is drawing it up.</summary>
    Designing,

    /// <summary>The design is signed off and the build can be scheduled.</summary>
    DesignApproved,

    /// <summary>Being built.</summary>
    Construction,

    /// <summary>Finished. The end of the line — nothing follows it.</summary>
    Completed
}
