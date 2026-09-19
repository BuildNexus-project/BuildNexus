namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Where a project sits in its lifecycle. Persisted as its string name in
/// <c>projects.status</c> and in <c>project_status_history</c>.
/// </summary>
/// <remarks>
/// <see cref="Pending"/> through <see cref="Completed"/> are the lifecycle US-06
/// defines, in the order a project moves through them. <see cref="Cancelled"/>
/// is off to the side: US-08 lets a project be closed out before the build
/// starts, and it is terminal like <see cref="Completed"/> but reached by a
/// different route. Which moves are actually permitted is
/// <see cref="ProjectStatusTransitions"/>'s business, not this type's — an enum
/// can only say what the states are.
/// <para>
/// Declaration order matches the lifecycle, but nothing depends on the
/// underlying numbers: the name is what is stored, sent and compared, so a
/// value added at the end here would not silently re-label existing rows.
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
    Completed,

    /// <summary>
    /// Closed out before construction started (US-08). Terminal: a cancelled
    /// project cannot move anywhere, and it is left out of the active project
    /// lists while staying viewable in its own right.
    /// </summary>
    Cancelled
}
