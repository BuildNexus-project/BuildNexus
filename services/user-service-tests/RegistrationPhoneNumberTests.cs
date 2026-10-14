using System.Net;
using System.Net.Http.Json;
using BuildNexus.UserService.Contracts;

namespace BuildNexus.UserService.Tests;

[Trait("Category", "Integration")]
[Collection(UserServiceCollection.Name)]
public class RegistrationPhoneNumberTests
{
    private readonly HttpClient _client;

    public RegistrationPhoneNumberTests(UserServiceFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("0771234567", "0771234567")]
    [InlineData("+94771234567", "+94771234567")]
    [InlineData(" +94771234567 ", "+94771234567")]
    public async Task Signup_saves_the_phone_number_and_login_reads_it_back(string phoneNumber, string expected)
    {
        var email = $"{UserServiceFactory.TestEmailPrefix}phone-{Guid.NewGuid():N}@example.com";
        var registration = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Phone Signup",
            email,
            password = "Str0ngPass",
            phoneNumber
        });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var user = await registration.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(expected, user.PhoneNumber);
        Assert.Equal("Client", user.Role);

        // Login reloads the user from SQL, proving the number was persisted.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Str0ngPass" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.Equal(expected, body.User.PhoneNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("call me")]
    [InlineData("077123456")]
    [InlineData("07712345678")]
    [InlineData("0112345678")]
    [InlineData("+9477123456")]
    [InlineData("+947712345678")]
    [InlineData("+94112345678")]
    [InlineData("+940771234567")]
    [InlineData("94771234567")]
    [InlineData("+94 77 123 4567")]
    [InlineData("077-123-4567")]
    [InlineData("077-123-4567 ext 8")]
    [InlineData("1234567890123456789012345678901")]
    public async Task Signup_refuses_a_blank_or_invalid_phone_number(string? phoneNumber)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Invalid Phone Signup",
            email = $"{UserServiceFactory.TestEmailPrefix}phone-{Guid.NewGuid():N}@example.com",
            password = "Str0ngPass",
            phoneNumber
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_requires_a_phone_number_in_the_request()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Missing Phone Signup",
            email = $"{UserServiceFactory.TestEmailPrefix}phone-{Guid.NewGuid():N}@example.com",
            password = "Str0ngPass"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
