using System.Text;
using BuildNexus.ApiGateway.Authorization;
using BuildNexus.ApiGateway.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// The policy every proxied route names in appsettings.json. YARP also
// understands the built-in "anonymous", which is what the login and
// registration route uses — a caller cannot present a token before it has one.
const string AuthenticatedPolicy = "authenticated";
const string FrontendCorsPolicy = "frontend";

var builder = WebApplication.CreateBuilder(args);

// The routing table — which path prefix reaches which service — is
// configuration rather than code, so a destination address can change per
// environment without rebuilding the image. See the "ReverseProxy" section of
// appsettings.json for the local-development defaults, and the environment
// variables set in infra/docker-compose.yml for the containerised ones.
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS is configured once, here. The five services sit behind this gateway and
// are never called from a browser directly, so none of them repeats it — and
// there is only one place to add an origin when the frontend moves host.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// The three settings of the shared convention, validated at startup so a
// missing or weak signing key stops the gateway immediately rather than
// turning every proxied request into a 401 at runtime.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must be configured.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must be configured.")
    .Validate(
        o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumSigningKeyBytes,
        $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes for HMAC-SHA256.")
    .ValidateOnStart();

// Resolved per request through EventsType below, so it can take an ILogger.
builder.Services.AddScoped<GatewayChallengeEvents>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Validation settings come from the bound JwtOptions rather than a snapshot
// read straight off the configuration here, so any source layered on later —
// User Secrets locally, the container's environment in a deployment — reaches
// the validating side too.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwt) =>
    {
        var jwtOptions = jwt.Value;

        // Keep the claims exactly as they were issued, so "sub" and "role" are
        // not rewritten into the longer WS-Federation claim URIs on their way
        // through. The services downstream read the token themselves, and it
        // must reach them byte for byte as the User Service signed it.
        bearerOptions.MapInboundClaims = false;

        // A refusal at the gateway answers with the same problem details a
        // service would have returned.
        bearerOptions.EventsType = typeof(GatewayChallengeEvents);

        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            // No grace period: an expired token is rejected the moment it expires.
            ClockSkew = TimeSpan.Zero
        };
    });

// Deny by default. A route added to appsettings.json later without an
// AuthorizationPolicy carries no authorization metadata of its own, so the
// fallback catches it — a forgotten line cannot quietly publish an internal
// service to the internet. Everything anonymous says so explicitly.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser());

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

app.UseCors(FrontendCorsPolicy);

// Authentication runs ahead of the proxy, so a request with a missing, expired
// or forged token is answered here and never reaches an internal service. A
// request that passes keeps its original Authorization header — YARP forwards
// it untouched — so each service still validates the token itself rather than
// trusting that something upstream did.
app.UseAuthentication();
app.UseAuthorization();

// Liveness probe for docker-compose and the deployment host. It answers from
// the gateway itself and deliberately does not call downstream, so one service
// being down never reports the gateway as unhealthy.
app.MapGet("/health", () => Results.Ok(new { service = "api-gateway", status = "healthy" }))
   .AllowAnonymous();

app.MapReverseProxy();

app.Run();

// Exposed so the integration tests can boot the real application host.
public partial class Program { }
