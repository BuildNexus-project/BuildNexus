using System.Text;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Data access (ADO.NET, direct SQL — no ORM)
builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();

// One producer for the process, held open. Building a Kafka producer starts
// background threads and a connection pool, so one per request would spend more
// on setup than on the publish itself.
builder.Services.AddSingleton<IProjectEventPublisher, KafkaProjectEventPublisher>();

// Resolved per request through EventsType below, so it can take an ILogger.
builder.Services.AddScoped<AuthorizationProblemEvents>();

// JWT settings, validated at startup so a missing or weak signing key fails the
// service immediately rather than turning every request into a 401 at runtime.
// This service validates tokens and never signs one, so there is no lifetime
// setting here: that is baked into a token's exp claim by the User Service.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must be configured.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must be configured.")
    .Validate(
        o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumSigningKeyBytes,
        $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes for HMAC-SHA256.")
    .ValidateOnStart();

// Broker address, validated at startup for the same reason as the JWT settings:
// a service that cannot say where Kafka is will publish nothing, and finding
// that out from a log line after the first project is submitted is too late.
builder.Services.AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers), "Kafka:BootstrapServers must be configured.")
    .Validate(o => o.MessageTimeoutMs > 0, "Kafka:MessageTimeoutMs must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Validation settings are taken from the bound JwtOptions rather than a snapshot
// read straight off the configuration here, so any source layered on later —
// User Secrets locally, an integration test's own values, the container's
// environment in a deployment — reaches the validating side too.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwt) =>
    {
        var jwtOptions = jwt.Value;

        // Keep the claims exactly as they were issued, so "sub" and "role" are
        // not rewritten into the longer WS-Federation claim URIs.
        bearerOptions.MapInboundClaims = false;

        // A refused request answers with problem details rather than the empty
        // body the handler writes by default.
        bearerOptions.EventsType = typeof(AuthorizationProblemEvents);

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
            ClockSkew = TimeSpan.Zero,
            // Without this, [Authorize(Roles = ...)] looks for the long
            // WS-Federation claim URI, never finds it, and silently refuses
            // every caller. Part of the shared convention.
            RoleClaimType = JwtOptions.RoleClaimType,
            NameClaimType = "name"
        };
    });

// Deny by default: an endpoint that declares nothing still demands a signed-in
// caller, so a controller added later cannot end up open to the world just
// because someone forgot the attribute. Everything anonymous — the health probe
// — says so explicitly with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "BuildNexus Project Service", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by the User Service's /api/auth/login."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

var app = builder.Build();

// Bring the schema up to date before anything reads or writes. A failure here
// stops the service rather than letting it serve requests against a schema it
// does not match.
DatabaseMigrator.Migrate(
    app.Configuration.GetConnectionString("ProjectDb")
        ?? throw new InvalidOperationException("Connection string 'ProjectDb' is not configured."),
    app.Services.GetRequiredService<ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "project-service", status = "healthy" }))
   .AllowAnonymous();

app.MapControllers();

app.Run();

// Exposed so the integration tests can boot the real application host.
public partial class Program { }
