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
const string FrontendCorsPolicy = "frontend";

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

var app = builder.Build();

app.UseCors(FrontendCorsPolicy);

// Liveness probe for docker-compose and the deployment host. It answers from
// the gateway itself and deliberately does not call downstream, so a single
// service being down never reports the gateway as unhealthy.
app.MapGet("/health", () => Results.Ok(new { service = "api-gateway", status = "healthy" }));

app.MapReverseProxy();

app.Run();
