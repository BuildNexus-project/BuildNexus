using System.Security.Claims;
using BuildNexus.ProjectService.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BuildNexus.ProjectService.Authorization;

/// <summary>
/// Gives the two refusals that ASP.NET Core answers with an empty body — 401 for
/// a missing or invalid token, 403 for a good token whose role is not allowed —
/// the same RFC 7807 problem details every other failure in this service
/// returns.
/// </summary>
/// <remarks>
/// A bare 403 with no body is indistinguishable from a request that went
/// nowhere, both to a developer reading the network tab and to the frontend,
/// which shows whatever <c>title</c> and <c>detail</c> it is given. Refusals are
/// also logged with the caller and the route, so an unexpected denial can be
/// traced to the role that caused it.
/// </remarks>
public class AuthorizationProblemEvents : JwtBearerEvents
{
    private readonly ILogger<AuthorizationProblemEvents> _logger;

    public AuthorizationProblemEvents(ILogger<AuthorizationProblemEvents> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// No token, or one that failed validation. Expiry is called out by name —
    /// it is the one cause the caller can do something about, and it tells them
    /// nothing they do not already hold.
    /// </summary>
    public override Task Challenge(JwtBearerChallengeContext context)
    {
        // Take the response over; the default writes a header and no body.
        context.HandleResponse();

        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;

        _logger.LogWarning(
            "Rejected an unauthenticated request to {Method} {Path}: {Reason}.",
            context.Request.Method,
            context.Request.Path,
            context.AuthenticateFailure?.Message ?? "no bearer token was supplied");

        // 401 without a challenge header is malformed HTTP, and HandleResponse
        // skips the one the handler would have written.
        context.Response.Headers.WWWAuthenticate = expired
            ? "Bearer error=\"invalid_token\", error_description=\"The access token has expired\""
            : "Bearer";

        return WriteProblem(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            "Not authenticated",
            expired
                ? "Your session has expired. Sign in again to continue."
                : "This endpoint requires a valid access token in the 'Authorization: Bearer' header.");
    }

    /// <summary>
    /// The token is valid but the role it carries is not one this endpoint
    /// accepts.
    /// </summary>
    public override Task Forbidden(ForbiddenContext context)
    {
        _logger.LogWarning(
            "Refused {Method} {Path} for user {UserId} in role {Role}: the endpoint does not allow that role.",
            context.Request.Method,
            context.Request.Path,
            context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "unknown",
            context.Principal?.FindFirstValue(JwtOptions.RoleClaimType) ?? "none");

        return WriteProblem(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "Not allowed for your role",
            "Your role does not permit this action. Contact an administrator if you believe it should.");
    }

    private static Task WriteProblem(HttpContext httpContext, int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(httpContext);
}
