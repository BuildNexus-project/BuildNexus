using System.Net;
using System.Net.Http.Json;
using BuildNexus.UserService.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// Covers the privilege boundary on registration: every signup creates a Client,
/// and the bootstrap Admin the seeder creates can actually log in.
/// </summary>
[Trait("Category", "Integration")]
[Collection(UserServiceCollection.Name)]
public class RegistrationRoleTests
{
    private readonly HttpClient _client;

    public RegistrationRoleTests(UserServiceFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Architect")]
    [InlineData("ProjectManager")]
    public async Task Register_ignores_a_self_assigned_role_and_creates_a_Client(string role)
    {
        var email = $"{UserServiceFactory.TestEmailPrefix}{Guid.NewGuid():N}@example.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Escalation Attempt",
            email,
            phoneNumber = "0771234567",
            password = "Str0ngPass",
            role
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Client", (await response.Content.ReadFromJsonAsync<UserResponse>())!.Role);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Str0ngPass" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        var token = new JsonWebTokenHandler().ReadJsonWebToken(body!.AccessToken);
        Assert.Equal("Client", token.GetClaim("role").Value);
    }

    [Fact]
    public async Task Register_without_a_role_creates_a_Client()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Legitimate User",
            email = $"{UserServiceFactory.TestEmailPrefix}{Guid.NewGuid():N}@example.com",
            phoneNumber = "0771234567",
            password = "Str0ngPass"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Client", (await response.Content.ReadFromJsonAsync<UserResponse>())!.Role);
    }

    [Fact]
    public async Task Seeded_admin_can_log_in_and_receives_a_token_carrying_the_Admin_role()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@buildnexus.local",
            password = "ChangeMe123!"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(body!.AccessToken);
        Assert.Equal("Admin", token.GetClaim("role").Value);
        Assert.Equal("BuildNexusAuth", token.Issuer);
    }

    private record LoginResponse(string AccessToken);
}
