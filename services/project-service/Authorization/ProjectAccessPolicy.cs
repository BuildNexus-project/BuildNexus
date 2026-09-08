using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Authorization;

/// <summary>
/// Who may see and move one particular project — the second US-06 acceptance
/// bullet: the owning client, the assigned staff, or an Admin.
/// </summary>
/// <remarks>
/// This is the half of authorization that <c>[Authorize(Roles = ...)]</c>
/// cannot express. The attribute answers "may an Architect call this endpoint
/// at all", which is a question about the route; this answers "may
/// <em>this</em> Architect open <em>that</em> project", which is a question
/// about a row and can only be asked once the row has been read.
/// <para>
/// Kept out of the controller so the rule can be read and tested on its own,
/// and so both endpoints that need it are provably asking the same question
/// rather than each growing their own version of it.
/// </para>
/// </remarks>
public static class ProjectAccessPolicy
{
    /// <summary>
    /// Whether the caller may see the project and its history.
    /// </summary>
    /// <remarks>
    /// The Client is in: it is their building. So are the Architect and the
    /// Project Manager actually put on it — being an Architect is not enough,
    /// it has to be this project's Architect. An Admin sees everything, which
    /// is what administering the platform means.
    /// </remarks>
    public static bool CanView(Project project, Guid userId, string? role) =>
        IsAdmin(role) || IsOwningClient(project, userId) || IsAssignedStaff(project, userId);

    /// <summary>
    /// Whether the caller may move the project to its next status.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="CanView"/> by exactly one case: the owning
    /// Client. They watch their project's progress and can see every step of
    /// it, but they do not declare their own design approved or their own build
    /// finished — that is the company's word, not the customer's.
    /// </remarks>
    public static bool CanUpdateStatus(Project project, Guid userId, string? role) =>
        IsAdmin(role) || IsAssignedStaff(project, userId);

    /// <summary>
    /// Whether the caller sees every project rather than only the ones they are
    /// party to. Admin only.
    /// </summary>
    public static bool SeesEveryProject(string? role) => IsAdmin(role);

    /// <summary>
    /// Whether the caller may cancel the project — US-08: the owning Client, or
    /// an Admin.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="CanUpdateStatus"/> in a different direction: the
    /// assigned Architect and Project Manager, who move a project forward, are
    /// deliberately out here. The decision to abandon a project before it is
    /// built is the customer's or the company's, not the people delivering it.
    /// </remarks>
    public static bool CanCancel(Project project, Guid userId, string? role) =>
        IsAdmin(role) || IsOwningClient(project, userId);

    /// <summary>
    /// Compared with <see cref="StringComparison.Ordinal"/>: the role claim is
    /// a wire value the User Service signs, not free text, and a case-insensitive
    /// match here would accept a token this service should not be trusting.
    /// </summary>
    private static bool IsAdmin(string? role) =>
        string.Equals(role, PlatformRoles.Admin, StringComparison.Ordinal);

    private static bool IsOwningClient(Project project, Guid userId) =>
        project.ClientId == userId;

    /// <summary>
    /// The Architect or Project Manager on this project. Both are <c>null</c>
    /// on every project today — nothing assigns staff yet — so this is
    /// currently always <c>false</c>, which is the correct answer rather than a
    /// gap: nobody is assigned, so nobody qualifies as assigned staff.
    /// </summary>
    private static bool IsAssignedStaff(Project project, Guid userId) =>
        project.AssignedArchitectId == userId || project.AssignedProjectManagerId == userId;
}
