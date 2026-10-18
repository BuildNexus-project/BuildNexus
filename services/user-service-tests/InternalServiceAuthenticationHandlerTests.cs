using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The API-key check that guards the internal endpoints other services call
/// (<see cref="InternalServiceAuthenticationHandler"/>).
/// </summary>
/// <remarks>
/// Added for mutation testing (SE3112-04). Stryker found that no unit test ran this
/// handler: all 10 of its mutants were "no coverage", including the ones that would
/// let a wrong key in. The handler is registered the same way Program.cs does it
/// and called through <c>HttpContext.AuthenticateAsync</c>, so no web host is needed.
/// </remarks>
public class InternalServiceAuthenticationHandlerTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task The_configured_key_authenticates_the_caller_as_an_internal_service()
    {
        var result = await AuthenticateWith(Key);

        Assert.True(result.Succeeded);
        Assert.Equal(InternalServiceAuthenticationHandler.SchemeName, result.Principal!.Identity!.AuthenticationType);
    }

    [Fact]
    public async Task A_request_with_no_key_header_is_refused_and_told_the_header_is_required()
    {
        var result = await AuthenticateWith();

        Assert.False(result.Succeeded);
        Assert.Equal("The X-Internal-Api-Key header is required.", result.Failure!.Message);
    }

    [Fact]
    public async Task A_request_sending_the_header_twice_is_refused_even_if_one_value_is_right()
    {
        var result = await AuthenticateWith(Key, "something-else");

        Assert.False(result.Succeeded);
        Assert.Equal("The X-Internal-Api-Key header is required.", result.Failure!.Message);
    }

    [Fact]
    public async Task A_wrong_key_of_the_right_length_is_refused()
    {
        // Same length, so only the byte comparison can refuse it.
        var result = await AuthenticateWith("x123456789abcdef0123456789abcdef");

        Assert.False(result.Succeeded);
        Assert.Equal("The internal API key is not valid.", result.Failure!.Message);
    }

    [Fact]
    public async Task A_key_that_only_starts_with_the_right_key_is_refused()
    {
        // Longer than the real key, so only the length check can refuse it.
        var result = await AuthenticateWith(Key + "-extra");

        Assert.False(result.Succeeded);
        Assert.Equal("The internal API key is not valid.", result.Failure!.Message);
    }

    // ---------------------------------------------------------- helpers ----

    /// <summary>Sends a request carrying the given values of the key header (none, one or several).</summary>
    private static Task<AuthenticateResult> AuthenticateWith(params string[] headerValues)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<InternalServiceOptions>(options => options.ApiKey = Key);
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, InternalServiceAuthenticationHandler>(
                InternalServiceAuthenticationHandler.SchemeName, configureOptions: null);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        if (headerValues.Length > 0)
        {
            httpContext.Request.Headers["X-Internal-Api-Key"] = headerValues;
        }

        return httpContext.AuthenticateAsync(InternalServiceAuthenticationHandler.SchemeName);
    }
}
