using System.Security.Claims;
using System.Text.Json;
using BuildNexus.PaymentService.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// What a caller is told when the JWT check turns them away: the 401 and 403 bodies
/// written by <see cref="AuthorizationProblemEvents"/>.
/// </summary>
/// <remarks>
/// Added for mutation testing (SE3112-04). Stryker found that no unit test ran this
/// class at all: all 13 of its mutants were "no coverage". The events are called
/// directly with a hand-built context, so no web host or token is needed.
/// </remarks>
public class AuthorizationProblemEventsTests
{
    private readonly AuthorizationProblemEvents _events = new(NullLogger<AuthorizationProblemEvents>.Instance);

    // ------------------------------------------------------------- 401 ----

    [Fact]
    public async Task A_request_with_no_token_gets_a_401_problem_asking_for_a_bearer_token()
    {
        var context = ChallengeContext(failure: null);

        await _events.Challenge(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());

        var problem = await ReadProblem(context.HttpContext);
        Assert.Equal("Not authenticated", problem.GetProperty("title").GetString());
        Assert.Equal(
            "This endpoint requires a valid access token in the 'Authorization: Bearer' header.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_expired_token_is_told_its_session_expired_rather_than_that_it_has_no_token()
    {
        // The difference matters to the frontend: an expired session should send the
        // user back to sign in, not show a generic "not allowed" message.
        var context = ChallengeContext(failure: new SecurityTokenExpiredException("Lifetime validation failed."));

        await _events.Challenge(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(
            "Bearer error=\"invalid_token\", error_description=\"The access token has expired\"",
            context.Response.Headers.WWWAuthenticate.ToString());

        var problem = await ReadProblem(context.HttpContext);
        Assert.Equal("Not authenticated", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Your session has expired. Sign in again to continue.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_invalid_but_unexpired_token_is_not_reported_as_expired()
    {
        // Any failure other than expiry, for example a bad signature, gets the plain answer.
        var context = ChallengeContext(failure: new SecurityTokenInvalidSignatureException("Bad signature."));

        await _events.Challenge(context);

        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task The_challenge_marks_the_response_as_handled_so_the_default_401_is_not_written_too()
    {
        var context = ChallengeContext(failure: null);

        await _events.Challenge(context);

        Assert.True(context.Handled);
    }

    // ------------------------------------------------------------- 403 ----

    [Fact]
    public async Task A_signed_in_user_in_the_wrong_role_gets_a_403_problem_naming_the_role_as_the_reason()
    {
        var context = ForbiddenContext();

        await _events.Forbidden(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        var problem = await ReadProblem(context.HttpContext);
        Assert.Equal("Not allowed for your role", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Your role does not permit this action. Contact an administrator if you believe it should.",
            problem.GetProperty("detail").GetString());
    }

    // ---------------------------------------------------------- helpers ----

    private static readonly AuthenticationScheme Scheme =
        new(JwtBearerDefaults.AuthenticationScheme, displayName: null, typeof(JwtBearerHandler));

    private static JwtBearerChallengeContext ChallengeContext(Exception? failure) =>
        new(NewHttpContext(), Scheme, new JwtBearerOptions(), new AuthenticationProperties())
        {
            AuthenticateFailure = failure
        };

    private static ForbiddenContext ForbiddenContext()
    {
        var httpContext = NewHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Client")], "TestAuth"));

        return new ForbiddenContext(httpContext, Scheme, new JwtBearerOptions());
    }

    /// <summary>
    /// A request with somewhere to write the response body, and the logging service
    /// that <c>Results.Problem</c> asks for when it writes.
    /// </summary>
    private static DefaultHttpContext NewHttpContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() }
    };

    private static async Task<JsonElement> ReadProblem(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);

        return document.RootElement.Clone();
    }
}
