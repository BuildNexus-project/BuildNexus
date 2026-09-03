using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// US-37 end to end against the real host: an administrator pages and filters
/// the directory, edits an account, deactivates one — and the account they
/// deactivated can no longer sign in.
/// </summary>
/// <remarks>
/// The controller tests cover what each action decides; these cover that the
/// decisions survive real HTTP, real JSON and real SQL. The last one is the
/// point of the story rather than a detail of it: deactivation only means
/// something if login honours it, and login is a different controller reading
/// the same column.
/// <para>
/// Needs the development database running:
/// <c>cd infra &amp;&amp; docker compose up -d user-db</c>. Every account these
/// tests register carries the factory's test prefix and is removed afterwards.
/// </para>
/// </remarks>
[Collection(UserServiceCollection.Name)]
public class AdminUserDirectoryTests
{
    private const string Password = "Str0ngPass";

    private readonly UserServiceFactory _factory;

    public AdminUserDirectoryTests(UserServiceFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_deactivated_user_cannot_log_in()
    {
        var admin = await SignedInAsAdminAsync();
        var (userId, email) = await RegisterAsync(PlatformRoles.Client);

        // Signing in works right up to the moment it is withdrawn.
        Assert.Equal(HttpStatusCode.OK, (await AttemptLoginAsync(email)).StatusCode);

        var deactivation = await admin.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivation.StatusCode);
        Assert.False((await ReadSummaryAsync(deactivation)).IsActive);

        var refused = await AttemptLoginAsync(email);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_user_is_refused_with_the_same_message_as_a_wrong_password()
    {
        // Which of the two it was is the account holder's business with their
        // administrator, not something the login endpoint tells the world.
        var admin = await SignedInAsAdminAsync();
        var (userId, email) = await RegisterAsync(PlatformRoles.Client);

        await admin.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false });

        var deactivated = await ReadProblemAsync(await AttemptLoginAsync(email));
        var wrongPassword = await ReadProblemAsync(await AttemptLoginAsync(email, "WrongPass123"));

        Assert.Equal(wrongPassword.Detail, deactivated.Detail);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_get_back_in_through_a_password_reset()
    {
        // Otherwise "forgot password" would quietly undo the deactivation.
        var admin = await SignedInAsAdminAsync();
        var (userId, email) = await RegisterAsync(PlatformRoles.Client);

        await admin.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false });

        // Still answered 202 — the endpoint says the same thing to everyone —
        // but no link is issued, so the password cannot be replaced.
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AttemptLoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task A_reinstated_user_can_log_in_again()
    {
        var admin = await SignedInAsAdminAsync();
        var (userId, email) = await RegisterAsync(PlatformRoles.Client);

        await admin.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false });
        var reinstatement = await admin.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = true });

        Assert.Equal(HttpStatusCode.OK, reinstatement.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AttemptLoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_deactivate_their_own_account()
    {
        var admin = await SignedInAsAdminAsync();
        var adminId = (await ReadAsync<UserResponse>(await admin.GetAsync("/api/users/me"))).Id;

        var response = await admin.PatchAsJsonAsync($"/api/users/{adminId}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And the account still works, which is the whole point of refusing.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task An_edit_moves_the_account_to_its_new_name_email_and_role()
    {
        var admin = await SignedInAsAdminAsync();
        var (userId, _) = await RegisterAsync(PlatformRoles.Client);
        var newEmail = TestEmail("moved");

        var response = await admin.PutAsJsonAsync($"/api/users/{userId}", new
        {
            fullName = "Nimal Fernando",
            email = newEmail,
            role = nameof(UserRole.ProjectManager)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadSummaryAsync(response);
        Assert.Equal("Nimal Fernando", updated.FullName);
        Assert.Equal(newEmail, updated.Email);
        Assert.Equal(nameof(UserRole.ProjectManager), updated.Role);

        // Read back through a second request, so this is what MySQL holds
        // rather than what the action happened to return.
        var stored = await ReadAsync<UserResponse>(await admin.GetAsync($"/api/users/{userId}"));
        Assert.Equal(newEmail, stored.Email);
        Assert.Equal(nameof(UserRole.ProjectManager), stored.Role);
    }

    [Fact]
    public async Task An_edited_email_is_the_one_the_user_signs_in_with()
    {
        // The email is the login identity, so moving it has to move the login
        // too — an edit that left the old address working would be a second,
        // invisible way in.
        var admin = await SignedInAsAdminAsync();
        var (userId, oldEmail) = await RegisterAsync(PlatformRoles.Client);
        var newEmail = TestEmail("renamed");

        await admin.PutAsJsonAsync($"/api/users/{userId}", new
        {
            fullName = "Nimal Fernando",
            email = newEmail,
            role = nameof(UserRole.Client)
        });

        Assert.Equal(HttpStatusCode.OK, (await AttemptLoginAsync(newEmail)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AttemptLoginAsync(oldEmail)).StatusCode);
    }

    [Fact]
    public async Task An_edit_onto_an_address_another_account_holds_is_refused()
    {
        var admin = await SignedInAsAdminAsync();
        var (userId, _) = await RegisterAsync(PlatformRoles.Client);
        var (_, takenEmail) = await RegisterAsync(PlatformRoles.Architect);

        var response = await admin.PutAsJsonAsync($"/api/users/{userId}", new
        {
            fullName = "Nimal Fernando",
            email = takenEmail,
            role = nameof(UserRole.Client)
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_edit_may_promote_a_user_to_admin_and_the_new_admin_can_administer()
    {
        // Self-service registration refuses the Admin role, so this endpoint is
        // the only way to grant it — and the grant has to be real, not just a
        // string in a column.
        var admin = await SignedInAsAdminAsync();
        var (userId, email) = await RegisterAsync(PlatformRoles.Client);

        await admin.PutAsJsonAsync($"/api/users/{userId}", new
        {
            fullName = "Promoted Admin",
            email,
            role = nameof(UserRole.Admin)
        });

        // A fresh token, since the old one still carries the old role claim.
        var promoted = await ClientWithTokenAsync(await LogInAsync(email));

        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task The_directory_hands_back_one_page_at_a_time()
    {
        var admin = await SignedInAsAdminAsync();
        await RegisterAsync(PlatformRoles.Architect);
        await RegisterAsync(PlatformRoles.Architect);

        var page = await ReadAsync<PagedResponse<UserSummaryResponse>>(
            await admin.GetAsync("/api/users?page=1&pageSize=1"));

        Assert.Single(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
        // Two Architects were just registered and a seeded Admin exists, so the
        // total is a count of the table rather than of the page handed back.
        Assert.True(page.TotalCount >= 3, $"Expected at least 3 accounts, the total said {page.TotalCount}.");
        Assert.Equal(page.TotalCount, page.TotalPages);
    }

    [Fact]
    public async Task The_directory_narrows_to_one_role_when_asked()
    {
        var admin = await SignedInAsAdminAsync();
        var (architectId, _) = await RegisterAsync(PlatformRoles.Architect);
        var (clientId, _) = await RegisterAsync(PlatformRoles.Client);

        var architects = await ReadEveryPageAsync(admin, $"/api/users?role={PlatformRoles.Architect}");

        Assert.All(architects, row => Assert.Equal(PlatformRoles.Architect, row.Role));
        Assert.Contains(architects, row => row.Id == architectId);
        Assert.DoesNotContain(architects, row => row.Id == clientId);
    }

    [Fact]
    public async Task The_directory_lists_deactivated_accounts_too()
    {
        // The one view that has to: reinstating an account starts with finding it.
        var admin = await SignedInAsAdminAsync();
        var (userId, _) = await RegisterAsync(PlatformRoles.Architect);

        await admin.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false });

        var architects = await ReadEveryPageAsync(admin, $"/api/users?role={PlatformRoles.Architect}");

        Assert.Contains(architects, row => row.Id == userId && !row.IsActive);
    }

    [Fact]
    public async Task A_page_past_the_end_is_an_empty_answer_rather_than_a_404()
    {
        var admin = await SignedInAsAdminAsync();

        var page = await ReadAsync<PagedResponse<UserSummaryResponse>>(
            await admin.GetAsync("/api/users?page=100000&pageSize=100"));

        Assert.Empty(page.Items);
        // The total is what tells the caller they overshot.
        Assert.True(page.TotalCount >= 1);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?role=Superuser")]
    public async Task A_directory_query_the_service_cannot_honour_is_refused(string query)
    {
        var admin = await SignedInAsAdminAsync();

        var response = await admin.GetAsync($"/api/users{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Administering_an_account_is_closed_to_every_other_role()
    {
        // The role gate sits on the service, not on the page in front of it.
        var (userId, _) = await RegisterAsync(PlatformRoles.Client);

        foreach (var role in new[] { PlatformRoles.Client, PlatformRoles.Architect, PlatformRoles.ProjectManager })
        {
            var (_, email) = await RegisterAsync(role);
            var caller = await ClientWithTokenAsync(await LogInAsync(email));

            var edit = await caller.PutAsJsonAsync($"/api/users/{userId}", new
            {
                fullName = "Nimal Fernando",
                email = TestEmail("nope"),
                role = nameof(UserRole.Admin)
            });

            var deactivation = await caller.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false });

            Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deactivation.StatusCode);
        }
    }

    // -------------------------------------------------------------- set-up ---

    private static string TestEmail(string label) =>
        $"{UserServiceFactory.TestEmailPrefix}{label}-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// Registers an account with the factory's test prefix, returning its id and
    /// the address it signs in with.
    /// </summary>
    private async Task<(Guid Id, string Email)> RegisterAsync(string role)
    {
        var email = TestEmail(role.ToLowerInvariant());

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            fullName = $"US-37 {role}",
            email,
            password = Password,
            role
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return ((await ReadAsync<UserResponse>(response)).Id, email);
    }

    /// <summary>
    /// A client carrying a real Admin token. Admin is not self-assignable, so
    /// the bootstrap account the seeder creates in Development is the only way
    /// to hold that role here.
    /// </summary>
    private async Task<HttpClient> SignedInAsAdminAsync() =>
        await ClientWithTokenAsync(await LogInAsync("admin@buildnexus.local", "ChangeMe123!"));

    private Task<HttpClient> ClientWithTokenAsync(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return Task.FromResult(client);
    }

    private Task<HttpResponseMessage> AttemptLoginAsync(string email, string password = Password) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<string> LogInAsync(string email, string password = Password)
    {
        var response = await AttemptLoginAsync(email, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadAsync<TokenBody>(response)).AccessToken;
    }

    /// <summary>
    /// Walks every page of a listing, so an assertion about who is in it does
    /// not depend on which page they landed on — accounts left behind by other
    /// classes in this collection shift the ordering.
    /// </summary>
    private static async Task<List<UserSummaryResponse>> ReadEveryPageAsync(HttpClient client, string route)
    {
        var rows = new List<UserSummaryResponse>();
        var page = 1;
        var separator = route.Contains('?') ? '&' : '?';

        while (true)
        {
            var body = await ReadAsync<PagedResponse<UserSummaryResponse>>(
                await client.GetAsync($"{route}{separator}page={page}&pageSize={UserListQuery.MaxPageSize}"));

            rows.AddRange(body.Items);

            if (page >= body.TotalPages)
            {
                return rows;
            }

            page++;
        }
    }

    private static async Task<UserSummaryResponse> ReadSummaryAsync(HttpResponseMessage response) =>
        await ReadAsync<UserSummaryResponse>(response);

    private static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response) =>
        await ReadAsync<ProblemBody>(response);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>();

        Assert.NotNull(body);

        return body!;
    }

    private record TokenBody(string AccessToken);

    private record ProblemBody(int Status, string? Title, string? Detail);
}
