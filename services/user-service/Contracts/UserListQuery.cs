using System.ComponentModel.DataAnnotations;
using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Query string for <c>GET /api/users</c>: which page of the Admin directory to
/// return, and optionally which role to narrow it to.
/// </summary>
/// <remarks>
/// Every field has a default, so a caller that asks for nothing gets the first
/// page of every role rather than an error.
/// </remarks>
public class UserListQuery
{
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The largest page the service will build. A caller asking for more is
    /// refused rather than quietly given fewer, so a page that comes back short
    /// always means the rows ran out.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Optional. Absent lists every role; a value narrows the page and the total
    /// alike, so the page count describes the filtered set.
    /// </summary>
    [PlatformRole]
    public string? Role { get; set; }

    /// <summary>1-based, so the first page is <c>?page=1</c> and not <c>?page=0</c>.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; set; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "Page size must be between 1 and 100.")]
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// The filter as the repository wants it, or <c>null</c> for "every role".
    /// Only ever called once validation has accepted the value.
    /// </summary>
    public UserRole? ParsedRole() =>
        string.IsNullOrWhiteSpace(Role) ? null : Enum.Parse<UserRole>(Role, ignoreCase: true);
}
