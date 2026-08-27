using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BuildNexus.ApiGateway.Authorization;

/// <summary>
/// Gives a token refused at the gateway the same RFC 7807 problem details the
/// services return, instead of the empty body the handler writes by default.
/// </summary>
/// <remarks>
/// The point of a single entry point is that the frontend cannot tell which
/// side of the gateway answered it. The React client reads <c>title</c> and
/// <c>detail</c> off every failure, so a bare 401 from here — with no body —
/// would surface as a generic "request failed" for exactly the case the user
/// can act on: an expired session. Refusals are logged with the route so a
/// request rejected before it was ever proxied can be told apart from one the
/// service itself turned down.
/// </remarks>
public class GatewayChallengeEvents : JwtBearerEvents
{
    private readonly ILogger<GatewayChallengeEvents> _logger;

    public GatewayChallengeEvents(ILogger<GatewayChallengeEvents> logger)
    {
        _logger = logger;
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        // Take the response over; the default writes a header and no body.
        context.HandleResponse();

        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;

        _logger.LogWarning(
            "Refused {Method} {Path} at the gateway; it was not proxied: {Reason}.",
            context.Request.Method,
            context.Request.Path,
            context.AuthenticateFailure?.Message ?? "no bearer token was supplied");

        // 401 without a challenge header is malformed HTTP, and HandleResponse
        // skips the one the handler would have written.
        context.Response.Headers.WWWAuthenticate = expired
            ? "Bearer error=\"invalid_token\", error_description=\"The access token has expired\""
            : "Bearer";

        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Not authenticated",
            detail: expired
                ? "Your session has expired. Sign in again to continue."
                : "This endpoint requires a valid access token in the 'Authorization: Bearer' header.")
            .ExecuteAsync(context.HttpContext);
    }
}
