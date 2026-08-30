namespace BuildNexus.ProjectService.Models;

/// <summary>
/// Where a project sits in its lifecycle. Persisted as its string name in
/// <c>projects.status</c>.
/// </summary>
/// <remarks>
/// <see cref="Pending"/> is the only value so far, and it is deliberately the
/// only one: US-05 creates a project awaiting review and says nothing about what
/// happens to it next. The story that adds approval or scheduling adds the value
/// here and the matching numbered migration script — inventing the rest now
/// would be guessing at a contract no story has defined.
/// </remarks>
public enum ProjectStatus
{
    /// <summary>Submitted by the Client and waiting on the company to pick it up.</summary>
    Pending
}
