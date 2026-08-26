using System.Net;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// Covers the privilege boundary on registration: nobody may hand themselves the
/// Admin role, and the bootstrap Admin the seeder creates can actually log in.
/// </summary>
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
    public async Task Register_rejects_an_attempt_to_self_assign_Admin(string role)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Escalation Attempt",
            email = $"{UserServiceFactory.TestEmailPrefix}{Guid.NewGuid():N}@example.com",
            password = "Str0ngPass",
            role
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_still_accepts_the_three_self_service_roles()
    {
        foreach (var role in new[] { "Client", "Architect", "ProjectManager" })
        {
            var response = await _client.PostAsJsonAsync("/api/auth/register", new
            {
                fullName = "Legitimate User",
                email = $"{UserServiceFactory.TestEmailPrefix}{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
                password = "Str0ngPass",
                role
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
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
