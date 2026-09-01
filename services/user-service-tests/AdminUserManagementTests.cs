using System.Security.Claims;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Controllers;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// What the three Admin account-management endpoints do with a payload that has
/// already passed validation (US-37): which page they ask for, what they save,
/// what they refuse, and how they answer. The repository is a stand-in, so these
/// run without a database.
/// </summary>
/// <remarks>
/// The role gate in front of all three is not retested here — it is declared on
/// the actions and exercised end to end in <see cref="RoleAccessTests"/> and by
/// reflection in <see cref="EndpointRoleDeclarationTests"/>.
/// </remarks>
public class AdminUserManagementTests
{
    // ------------------------------------------------------------- listing ---

    [Fact]
    public async Task The_directory_asks_for_the_page_the_query_named()
    {
        var (controller, repository, _) = ControllerFor();

        await controller.GetAll(new UserListQuery { Role = nameof(UserRole.Architect), Page = 3, PageSize = 5 });

        Assert.Equal(UserRole.Architect, repository.RequestedRole);
        Assert.Equal(3, repository.RequestedPage);
        Assert.Equal(5, repository.RequestedPageSize);
    }

    [Fact]
    public async Task The_directory_defaults_to_the_first_page_of_every_role()
    {
        // A caller that asks for nothing gets a usable answer rather than an
        // error, and never the whole table.
        var (controller, repository, _) = ControllerFor();

        await controller.GetAll(new UserListQuery());

        Assert.Null(repository.RequestedRole);
        Assert.Equal(1, repository.RequestedPage);
        Assert.Equal(UserListQuery.DefaultPageSize, repository.RequestedPageSize);
    }

    [Fact]
    public async Task The_directory_reports_the_total_beside_the_page()
    {
        // Without the total, a caller cannot tell a last page from a full one.
        var (controller, repository, _) = ControllerFor();
        repository.TotalCount = 47;

        var body = PageBody(await controller.GetAll(new UserListQuery { Page = 2, PageSize = 20 }));

        Assert.Equal(2, body.Page);
        Assert.Equal(20, body.PageSize);
        Assert.Equal(47, body.TotalCount);
        Assert.Equal(3, body.TotalPages);
    }

    [Fact]
    public async Task An_empty_directory_still_reads_as_one_page()
    {
        var (controller, repository, _) = ControllerFor();
        repository.Users.Clear();
        repository.TotalCount = 0;

        var body = PageBody(await controller.GetAll(new UserListQuery()));

        Assert.Empty(body.Items);
        // "Page 1 of 0" reads as a bug to anyone looking at it.
        Assert.Equal(1, body.TotalPages);
    }

    [Fact]
    public async Task The_directory_lists_deactivated_accounts_too()
    {
        // The one view that has to show them: reinstating an account starts
        // with finding it.
        var (controller, repository, _) = ControllerFor();
        repository.Users.Single(user => user.Id == TargetId).IsActive = false;

        var body = PageBody(await controller.GetAll(new UserListQuery()));

        Assert.Contains(body.Items, row => row.Id == TargetId && !row.IsActive);
    }

    // ---------------------------------------------------------------- edit ---

    [Fact]
    public async Task An_edit_saves_the_new_name_email_and_role()
    {
        var (controller, repository, target) = ControllerFor();

        var result = await controller.UpdateUser(TargetId, new AdminUpdateUserRequest
        {
            FullName = "Nimal Perera",
            Email = "nimal.perera@example.com",
            Role = nameof(UserRole.ProjectManager)
        });

        Assert.True(repository.AccountWasUpdated);
        Assert.Equal("Nimal Perera", target.FullName);
        Assert.Equal("nimal.perera@example.com", target.Email);
        Assert.Equal(UserRole.ProjectManager, target.Role);

        var body = SummaryBody(result);
        Assert.Equal("Nimal Perera", body.FullName);
        Assert.Equal("nimal.perera@example.com", body.Email);
        Assert.Equal(nameof(UserRole.ProjectManager), body.Role);
    }

    [Fact]
    public async Task An_edit_trims_the_name_and_normalises_the_email()
    {
        // The same canonical form registration and login use, or the account
        // would be moved to an address its owner could never sign in with.
        var (controller, _, target) = ControllerFor();

        await controller.UpdateUser(TargetId, EditTo(fullName: "  Nimal Perera  ", email: "  Nimal.Perera@Example.COM "));

        Assert.Equal("Nimal Perera", target.FullName);
        Assert.Equal("nimal.perera@example.com", target.Email);
    }

    [Fact]
    public async Task An_edit_leaves_the_password_and_the_active_flag_alone()
    {
        // Nobody, administrator included, sets another person's password, and
        // an edit is not a way to reinstate a deactivated account.
        var (controller, _, target) = ControllerFor();
        target.IsActive = false;
        var storedHash = target.PasswordHash;

        await controller.UpdateUser(TargetId, EditTo(role: nameof(UserRole.Admin)));

        Assert.Equal(storedHash, target.PasswordHash);
        Assert.False(target.IsActive);
    }

    [Fact]
    public async Task An_edit_may_promote_someone_to_Admin()
    {
        // The one path to a role self-service registration refuses.
        var (controller, _, target) = ControllerFor();

        await controller.UpdateUser(TargetId, EditTo(role: nameof(UserRole.Admin)));

        Assert.Equal(UserRole.Admin, target.Role);
    }

    [Fact]
    public async Task An_edit_stamps_the_row_as_changed()
    {
        var (controller, _, target) = ControllerFor();
        var before = target.UpdatedAt;

        await controller.UpdateUser(TargetId, EditTo());

        Assert.True(target.UpdatedAt > before);
    }

    [Fact]
    public async Task An_edit_onto_an_address_another_account_holds_is_refused()
    {
        var (controller, repository, _) = ControllerFor();

        var result = await controller.UpdateUser(TargetId, EditTo(email: AdminEmail));

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ConflictObjectResult>(result).StatusCode);
        Assert.False(repository.AccountWasUpdated);
    }

    [Fact]
    public async Task An_edit_that_keeps_the_current_address_is_not_a_conflict()
    {
        // Renaming someone without touching their email must not collide with
        // their own row.
        var (controller, repository, target) = ControllerFor();

        var result = await controller.UpdateUser(TargetId, EditTo(fullName: "Nimal J. Perera", email: target.Email));

        Assert.IsType<OkObjectResult>(result);
        Assert.True(repository.AccountWasUpdated);
    }

    [Fact]
    public async Task An_edit_losing_the_race_for_an_address_is_refused_too()
    {
        // Two administrators moving two accounts onto one address at the same
        // moment: the unique index rejects the loser, and that has to read the
        // same way to the caller as losing the check above.
        var (controller, repository, _) = ControllerFor();
        repository.UpdateHitsTheUniqueIndex = true;

        var result = await controller.UpdateUser(TargetId, EditTo(email: "brand.new@example.com"));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task An_admin_cannot_change_their_own_role()
    {
        // Only an Admin reaches this endpoint, so self-demotion is the one edit
        // that could leave the platform with nobody able to undo it.
        var (controller, repository, _) = ControllerFor();

        var result = await controller.UpdateUser(AdminId, EditTo(
            fullName: "Site Admin", email: AdminEmail, role: nameof(UserRole.Client)));

        var problem = ValidationProblemBody(result);
        Assert.Contains(nameof(AdminUpdateUserRequest.Role), problem.Errors.Keys);
        Assert.False(repository.AccountWasUpdated);
    }

    [Fact]
    public async Task An_admin_may_still_correct_their_own_name_and_email()
    {
        // The guard is about the role, not about the account: an administrator
        // fixing a typo in their own name is not a lockout risk.
        var (controller, _, _) = ControllerFor();

        var result = await controller.UpdateUser(AdminId, EditTo(
            fullName: "Site Administrator", email: AdminEmail, role: nameof(UserRole.Admin)));

        Assert.Equal("Site Administrator", SummaryBody(result).FullName);
    }

    [Fact]
    public async Task An_edit_answers_404_for_an_account_that_does_not_exist()
    {
        var (controller, _, _) = ControllerFor();

        var result = await controller.UpdateUser(Guid.NewGuid(), EditTo());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task An_edit_answers_404_when_the_account_is_removed_mid_request()
    {
        var (controller, repository, _) = ControllerFor();
        repository.RowVanishesOnWrite = true;

        var result = await controller.UpdateUser(TargetId, EditTo());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task An_edit_answers_401_when_the_token_carries_no_usable_subject()
    {
        // The self-edit guards are read off the token's subject, so an action
        // that cannot identify its caller must not write anything.
        var (controller, repository, _) = ControllerFor(subject: "not-a-guid");

        var result = await controller.UpdateUser(TargetId, EditTo());

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(repository.AccountWasUpdated);
    }

    // ---------------------------------------------------------- deactivate ---

    [Fact]
    public async Task Deactivating_withdraws_the_account_s_access()
    {
        var (controller, repository, target) = ControllerFor();

        var result = await controller.SetUserActive(TargetId, new SetUserActiveRequest { IsActive = false });

        Assert.False(repository.LastActiveFlag);
        Assert.False(target.IsActive);
        Assert.False(SummaryBody(result).IsActive);
    }

    [Fact]
    public async Task Reinstating_gives_it_back()
    {
        // Deactivation is not a one-way door: the same endpoint undoes it.
        var (controller, _, target) = ControllerFor();
        target.IsActive = false;

        var result = await controller.SetUserActive(TargetId, new SetUserActiveRequest { IsActive = true });

        Assert.True(target.IsActive);
        Assert.True(SummaryBody(result).IsActive);
    }

    [Fact]
    public async Task Deactivating_touches_nothing_but_the_flag()
    {
        var (controller, _, target) = ControllerFor();

        await controller.SetUserActive(TargetId, new SetUserActiveRequest { IsActive = false });

        Assert.Equal("Nimal Fernando", target.FullName);
        Assert.Equal("nimal@example.com", target.Email);
        Assert.Equal(UserRole.Architect, target.Role);
    }

    [Fact]
    public async Task Deactivating_stamps_the_row_as_changed()
    {
        var (controller, _, target) = ControllerFor();
        var before = target.UpdatedAt;

        await controller.SetUserActive(TargetId, new SetUserActiveRequest { IsActive = false });

        Assert.True(target.UpdatedAt > before);
    }

    [Fact]
    public async Task An_admin_cannot_deactivate_their_own_account()
    {
        // This one rule is what guarantees an active administrator always
        // remains — the caller.
        var (controller, repository, _) = ControllerFor();

        var result = await controller.SetUserActive(AdminId, new SetUserActiveRequest { IsActive = false });

        var problem = ValidationProblemBody(result);
        Assert.Contains(nameof(SetUserActiveRequest.IsActive), problem.Errors.Keys);
        Assert.Null(repository.LastActiveFlag);
    }

    [Fact]
    public async Task An_admin_may_deactivate_another_admin()
    {
        // Two administrators is not a stalemate: the guard is about the caller's
        // own account, and the one doing the deactivating is still active.
        var (controller, _, _) = ControllerFor();
        var secondAdmin = new User
        {
            Id = Guid.Parse("2f9619ff-8b86-d011-b42d-00cf4fc964ff"),
            FullName = "Second Admin",
            Email = "second.admin@example.com",
            Role = UserRole.Admin,
            IsActive = true
        };

        var (withTwoAdmins, _, _) = ControllerFor(extraUser: secondAdmin);

        var result = await withTwoAdmins.SetUserActive(secondAdmin.Id, new SetUserActiveRequest { IsActive = false });

        Assert.False(SummaryBody(result).IsActive);
    }

    [Fact]
    public async Task Deactivating_answers_404_for_an_account_that_does_not_exist()
    {
        var (controller, _, _) = ControllerFor();

        var result = await controller.SetUserActive(Guid.NewGuid(), new SetUserActiveRequest { IsActive = false });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Deactivating_answers_404_when_the_account_is_removed_mid_request()
    {
        var (controller, repository, _) = ControllerFor();
        repository.RowVanishesOnWrite = true;

        var result = await controller.SetUserActive(TargetId, new SetUserActiveRequest { IsActive = false });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Deactivating_answers_401_when_the_token_carries_no_usable_subject()
    {
        var (controller, repository, _) = ControllerFor(subject: "not-a-guid");

        var result = await controller.SetUserActive(TargetId, new SetUserActiveRequest { IsActive = false });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Null(repository.LastActiveFlag);
    }

    // -------------------------------------------------------------- set-up ---

    /// <summary>The signed-in administrator making the calls.</summary>
    private static readonly Guid AdminId = Guid.Parse("1f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private const string AdminEmail = "admin@buildnexus.local";

    /// <summary>The account being administered.</summary>
    private static readonly Guid TargetId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static readonly DateTime Registered = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>An edit that changes nothing unless a test says what to change.</summary>
    private static AdminUpdateUserRequest EditTo(
        string fullName = "Nimal Fernando",
        string email = "nimal@example.com",
        string role = nameof(UserRole.Architect)) => new()
    {
        FullName = fullName,
        Email = email,
        Role = role
    };

    private static (UsersController Controller, StubUserRepository Repository, User Target) ControllerFor(
        string? subject = null,
        User? extraUser = null)
    {
        var admin = new User
        {
            Id = AdminId,
            FullName = "Site Admin",
            Email = AdminEmail,
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = Registered,
            UpdatedAt = Registered
        };

        var target = new User
        {
            Id = TargetId,
            FullName = "Nimal Fernando",
            Email = "nimal@example.com",
            PasswordHash = "stored-hash",
            Role = UserRole.Architect,
            IsActive = true,
            CreatedAt = Registered,
            UpdatedAt = Registered
        };

        var repository = new StubUserRepository([admin, target, .. extraUser is null ? Array.Empty<User>() : [extraUser]]);

        var identity = new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, subject ?? AdminId.ToString())],
            authenticationType: "Test");

        var controller = new UsersController(repository, NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };

        return (controller, repository, target);
    }

    private static PagedResponse<UserSummaryResponse> PageBody(IActionResult result) =>
        Assert.IsType<PagedResponse<UserSummaryResponse>>(Assert.IsType<OkObjectResult>(result).Value);

    private static UserSummaryResponse SummaryBody(IActionResult result) =>
        Assert.IsType<UserSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);

    private static ValidationProblemDetails ValidationProblemBody(IActionResult result) =>
        Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);

    /// <summary>
    /// Holds a handful of users in memory. The writes record that they were
    /// called and mutate the same instances the tests hold, which is what those
    /// tests inspect.
    /// </summary>
    private class StubUserRepository : IUserRepository
    {
        public StubUserRepository(IEnumerable<User> users) => Users = [.. users];

        public List<User> Users { get; }

        /// <summary>Set to reproduce the account being deleted between the read and the write.</summary>
        public bool RowVanishesOnWrite { get; set; }

        /// <summary>Set to reproduce two edits racing for one email address.</summary>
        public bool UpdateHitsTheUniqueIndex { get; set; }

        /// <summary>Overrides the total, so a page can be tested against a set bigger than the stub holds.</summary>
        public int? TotalCount { get; set; }

        public UserRole? RequestedRole { get; private set; }

        public int RequestedPage { get; private set; }

        public int RequestedPageSize { get; private set; }

        public bool AccountWasUpdated { get; private set; }

        /// <summary>The flag the last set-active call carried, or <c>null</c> if it was never reached.</summary>
        public bool? LastActiveFlag { get; private set; }

        public Task<PagedResult<User>> ListPageAsync(UserRole? role, int page, int pageSize)
        {
            RequestedRole = role;
            RequestedPage = page;
            RequestedPageSize = pageSize;

            var matching = Users.Where(user => role is null || user.Role == role).ToList();

            return Task.FromResult(new PagedResult<User>
            {
                Items = [.. matching.OrderBy(user => user.FullName).Skip((page - 1) * pageSize).Take(pageSize)],
                TotalCount = TotalCount ?? matching.Count
            });
        }

        public Task<bool> UpdateAccountAsync(User user)
        {
            if (UpdateHitsTheUniqueIndex)
            {
                throw new DuplicateEmailException(user.Email);
            }

            AccountWasUpdated = !RowVanishesOnWrite;

            return Task.FromResult(!RowVanishesOnWrite);
        }

        public Task<bool> SetActiveAsync(Guid userId, bool isActive, DateTime updatedAtUtc)
        {
            LastActiveFlag = isActive;

            return Task.FromResult(!RowVanishesOnWrite);
        }

        public Task<User?> GetByIdAsync(Guid id) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == id));

        public Task<bool> EmailExistsAsync(string email) =>
            Task.FromResult(Users.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<User?> GetByEmailAsync(string email) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Email == email));

        public Task<bool> AdminExistsAsync() => Task.FromResult(true);

        public Task<IReadOnlyList<User>> ListActiveByRolesAsync(IReadOnlyCollection<UserRole> roles) =>
            Task.FromResult<IReadOnlyList<User>>([]);

        public Task InsertAsync(User user) => Task.CompletedTask;

        // Neither belongs to an administrator: a user maintains their own
        // contact details, and only a reset link moves a password.
        public Task<bool> UpdateProfileAsync(User user) => Task.FromResult(true);

        public Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, DateTime updatedAtUtc) =>
            Task.FromResult(false);
    }
}
