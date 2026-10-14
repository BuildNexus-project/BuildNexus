using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Data;

/// <summary>Read-only query behind the Admin dashboard's user counts (US-21).</summary>
/// <remarks>
/// Kept apart from <see cref="IUserRepository"/>: this is an aggregate read shaped
/// for a screen, and nothing that signs a person in or edits an account has any
/// business being able to reach it.
/// </remarks>
public interface IUserDashboardRepository
{
    /// <summary>
    /// Every account counted by role and by whether it is active. The database
    /// does the counting, so the cost does not grow with the number of accounts.
    /// </summary>
    Task<IReadOnlyList<UserCountGroup>> CountByRoleAndStatusAsync(
        CancellationToken cancellationToken = default);
}
