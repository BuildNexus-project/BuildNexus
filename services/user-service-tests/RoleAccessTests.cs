using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildNexus.UserService.Authorization;
using Microsoft.AspNetCore.Http;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The role matrix for US-03, exercised end to end against the real host: for
/// each of the four platform roles, an action it is allowed and an action it is
/// not.
/// </summary>
/// <remarks>
/// Needs the development database running:
/// <c>cd infra &amp;&amp; docker compose up -d user-db</c>. Every account these
/// tests register carries the factory's test prefix and is removed afterwards.
/// </remarks>
[Collection(UserServiceCollection.Name)]
public class RoleAccessTests
{
    private const string OwnProfile = "/api/users/me";
    private const string ProjectStaffDirectory = "/api/users/directory";
    private const string AdminUserList = "/api/users";

    /// <summary>An id that need not exist: the role check runs long before the lookup.</summary>
    private const string SomeAccount = "/api/users/6f9619ff-8b86-d011-b42d-00cf4fc964ff";

    private const string Password = "Str0ngPass";

    private readonly UserServiceFactory _factory;

    public RoleAccessTests(UserServiceFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_Client_may_read_its_own_profile_but_neither_directory()
    {
        var client = await SignedInAsAsync(PlatformRoles.Client);

        await AssertAllowedAsync(client, OwnProfile);

        // A customer has no business browsing the firm's staff, let alone every
        // account on the platform.
        await AssertRefusedAsync(client, ProjectStaffDirectory);
        await AssertRefusedAsync(client, AdminUserList);
    }

    [Fact]
    public async Task An_Architect_may_read_the_project_staff_directory_but_not_the_admin_list()
    {
        var client = await SignedInAsAsync(PlatformRoles.Architect);

        await AssertAllowedAsync(client, ProjectStaffDirectory);
        await AssertRefusedAsync(client, AdminUserList);
    }

    [Fact]
    public async Task A_ProjectManager_may_read_the_project_staff_directory_but_not_another_account()
    {
        var client = await SignedInAsAsync(PlatformRoles.ProjectManager);

        await AssertAllowedAsync(client, ProjectStaffDirectory);
        await AssertRefusedAsync(client, AdminUserList);
        await AssertRefusedAsync(client, SomeAccount);
    }

    [Fact]
    public async Task An_Admin_may_read_every_account_but_not_the_project_staff_directory()
    {
        var client = await SignedInAsAsync(PlatformRoles.Admin);

        await AssertAllowedAsync(client, AdminUserList);

        // An Admin administers accounts; it is not a project participant, so the
        // staffing lookup is closed to it as firmly as the admin list is to
        // everyone else.
        await AssertRefusedAsync(client, ProjectStaffDirectory);
    }

    [Fact]
    public async Task Every_role_may_read_its_own_profile()
    {
        foreach (var role in PlatformRoles.All)
        {
            var client = await SignedInAsAsync(role);

            await AssertAllowedAsync(client, OwnProfile);
        }
    }

    [Fact]
    public async Task A_refusal_carries_problem_details_rather_than_an_empty_body()
    {
        var client = await SignedInAsAsync(PlatformRoles.Client);

        var response = await client.GetAsync(AdminUserList);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // "Not a silent failure": the caller is told what happened, in the same
        // problem-details shape every other failure in this service uses.
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status403Forbidden, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task A_request_with_no_token_is_401_rather_than_403()
    {
        // 401 and 403 are different answers to different questions — "who are
        // you" against "you may not". A role check must not swallow the first.
        var response = await _factory.CreateClient().GetAsync(AdminUserList);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An allowed action goes through and returns the resource, not a redirect or an empty 204.</summary>
    private static async Task AssertAllowedAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A disallowed action is refused with 403 — never quietly answered with the
    /// resource, and never mistaken for "not signed in".
    /// </summary>
    private static async Task AssertRefusedAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Returns a client carrying a real token for this role.</summary>
    private async Task<HttpClient> SignedInAsAsync(string role)
    {
        var client = _factory.CreateClient();

        var token = role == PlatformRoles.Admin
            // Admin is not self-assignable, so the bootstrap account the seeder
            // creates in Development is the only way to hold that role here.
            ? await LogInAsync(client, "admin@buildnexus.local", "ChangeMe123!")
            : await RegisterAndLogInAsync(client, role);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<string> RegisterAndLogInAsync(HttpClient client, string role)
    {
        var email = $"{UserServiceFactory.TestEmailPrefix}{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";

        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = $"Role Matrix {role}",
            email,
            password = Password,
            role
        });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        return await LogInAsync(client, email, Password);
    }

    private static async Task<string> LogInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        Assert.NotNull(body);

        return body!.AccessToken;
    }

    private record TokenBody(string AccessToken);

    private record ProblemBody(int Status, string? Title, string? Detail);
}
