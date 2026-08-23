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
/// What <c>PUT /api/users/me</c> does with a payload that has already passed
/// validation: what it saves, what it refuses to touch, and how it answers.
/// The repository is a stand-in, so these run without a database.
/// </summary>
public class UsersControllerProfileTests
{
    [Fact]
    public async Task Update_saves_the_new_name_and_contact_details()
    {
        var (controller, repository, user) = ControllerFor(SignedUpUser());

        var result = await controller.UpdateCurrentUser(new UpdateProfileRequest
        {
            FullName = "Ada Perera-Silva",
            PhoneNumber = "+94 77 123 4567",
            ContactAddress = "12 Galle Road, Colombo 03"
        });

        Assert.True(repository.ProfileWasUpdated);
        Assert.Equal("Ada Perera-Silva", user.FullName);
        Assert.Equal("+94 77 123 4567", user.PhoneNumber);
        Assert.Equal("12 Galle Road, Colombo 03", user.ContactAddress);

        var body = OkBody(result);
        Assert.Equal("Ada Perera-Silva", body.FullName);
        Assert.Equal("+94 77 123 4567", body.PhoneNumber);
        Assert.Equal("12 Galle Road, Colombo 03", body.ContactAddress);
    }

    [Fact]
    public async Task Update_trims_the_name_before_saving_it()
    {
        var (controller, _, user) = ControllerFor(SignedUpUser());

        await controller.UpdateCurrentUser(new UpdateProfileRequest { FullName = "  Ada Perera  " });

        Assert.Equal("Ada Perera", user.FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_clears_a_contact_detail_that_arrives_blank(string? blank)
    {
        var stored = SignedUpUser();
        stored.PhoneNumber = "0771234567";
        stored.ContactAddress = "12 Galle Road, Colombo 03";

        var (controller, _, user) = ControllerFor(stored);

        await controller.UpdateCurrentUser(new UpdateProfileRequest
        {
            FullName = "Ada Perera",
            PhoneNumber = blank,
            ContactAddress = blank
        });

        // Cleared means NULL in the column, not an empty string sitting in it.
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.ContactAddress);
    }

    [Fact]
    public async Task Update_leaves_the_email_and_role_alone_even_if_the_payload_carries_them()
    {
        // Validation refuses this payload long before the action is entered.
        // Asserted here anyway: the action must not be the only thing standing
        // between a caller and their own role.
        var (controller, _, user) = ControllerFor(SignedUpUser());

        var result = await controller.UpdateCurrentUser(new UpdateProfileRequest
        {
            FullName = "Ada Perera",
            Email = "someone.else@example.com",
            Role = nameof(UserRole.Admin)
        });

        Assert.Equal("ada@example.com", user.Email);
        Assert.Equal(UserRole.Architect, user.Role);

        var body = OkBody(result);
        Assert.Equal("ada@example.com", body.Email);
        Assert.Equal(nameof(UserRole.Architect), body.Role);
    }

    [Fact]
    public async Task Update_stamps_the_row_as_changed()
    {
        var stored = SignedUpUser();
        var before = stored.UpdatedAt;

        var (controller, _, user) = ControllerFor(stored);

        await controller.UpdateCurrentUser(new UpdateProfileRequest { FullName = "Ada Perera" });

        Assert.True(user.UpdatedAt > before);
    }

    [Fact]
    public async Task Update_answers_404_when_the_account_no_longer_exists()
    {
        // A token outliving the account it was issued for: still correctly
        // signed, but there is nothing left to update.
        var (controller, _, _) = ControllerFor(storedUser: null);

        var result = await controller.UpdateCurrentUser(new UpdateProfileRequest { FullName = "Ada Perera" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_answers_404_when_the_account_is_removed_mid_request()
    {
        var (controller, repository, _) = ControllerFor(SignedUpUser());
        repository.RowVanishesOnUpdate = true;

        var result = await controller.UpdateCurrentUser(new UpdateProfileRequest { FullName = "Ada Perera" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_answers_401_when_the_token_carries_no_usable_subject()
    {
        var (controller, repository, _) = ControllerFor(SignedUpUser(), subject: "not-a-guid");

        var result = await controller.UpdateCurrentUser(new UpdateProfileRequest { FullName = "Ada Perera" });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(repository.ProfileWasUpdated);
    }

    private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static User SignedUpUser() => new()
    {
        Id = UserId,
        FullName = "Ada Perera",
        Email = "ada@example.com",
        Role = UserRole.Architect,
        IsActive = true,
        CreatedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// Builds the controller with a signed-in caller, returning the stored user
    /// so a test can see what the action wrote to it.
    /// </summary>
    private static (UsersController Controller, StubUserRepository Repository, User Stored) ControllerFor(
        User? storedUser,
        string? subject = null)
    {
        var repository = new StubUserRepository(storedUser);

        var identity = new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, subject ?? UserId.ToString())],
            authenticationType: "Test");

        var controller = new UsersController(repository, NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };

        return (controller, repository, storedUser ?? new User());
    }

    private static UserResponse OkBody(IActionResult result) =>
        Assert.IsType<UserResponse>(Assert.IsType<OkObjectResult>(result).Value);

    /// <summary>
    /// Holds one user in memory. <see cref="UpdateProfileAsync"/> records that it
    /// was called rather than writing anything: the action mutates the same
    /// instance, which is what the tests inspect.
    /// </summary>
    private class StubUserRepository : IUserRepository
    {
        private readonly User? _user;

        public StubUserRepository(User? user) => _user = user;

        /// <summary>Set to reproduce the account being deleted between the read and the write.</summary>
        public bool RowVanishesOnUpdate { get; set; }

        public bool ProfileWasUpdated { get; private set; }

        public Task<bool> UpdateProfileAsync(User user)
        {
            ProfileWasUpdated = !RowVanishesOnUpdate;

            return Task.FromResult(!RowVanishesOnUpdate);
        }

        public Task<User?> GetByIdAsync(Guid id) =>
            Task.FromResult(_user is not null && _user.Id == id ? _user : null);

        public Task<bool> EmailExistsAsync(string email) => Task.FromResult(false);

        public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(null);

        public Task<bool> AdminExistsAsync() => Task.FromResult(true);

        public Task InsertAsync(User user) => Task.CompletedTask;
    }
}
