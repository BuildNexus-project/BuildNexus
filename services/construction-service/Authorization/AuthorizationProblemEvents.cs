using System.Security.Claims;
using BuildNexus.ConstructionService.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BuildNexus.ConstructionService.Authorization;

/// <summary>
/// Gives the two refusals ASP.NET Core answers with an empty body — 401 for a
/// missing or invalid token, 403 for a good token whose role is not allowed —
/// the same RFC 7807 problem details every other BuildNexus service returns.
/// </summary>
public class AuthorizationProblemEvents : JwtBearerEvents
{
    private readonly ILogger<AuthorizationProblemEvents> _logger;

    public AuthorizationProblemEvents(ILogger<AuthorizationProblemEvents> logger)
    {
        _logger = logger;
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;

        _logger.LogWarning(
            "Rejected an unauthenticated request to {Method} {Path}: {Reason}.",
            context.Request.Method,
            context.Request.Path,
            context.AuthenticateFailure?.Message ?? "no bearer token was supplied");

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
