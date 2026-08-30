using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// Data access for the <c>projects</c> table.
/// </summary>
public interface IProjectRepository
{
    /// <summary>Inserts a new project row.</summary>
    Task InsertAsync(Project project);
}
