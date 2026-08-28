using System.Text;
using BuildNexus.UserService.Authorization;
using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Services;
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
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();

// Security services
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// Resolved per request through EventsType below, so it can take an ILogger.
builder.Services.AddScoped<AuthorizationProblemEvents>();

// JWT settings, validated at startup so a missing or weak signing key fails
// the service immediately instead of at the first login attempt.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must be configured.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must be configured.")
    .Validate(
        o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumSigningKeyBytes,
        $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes for HMAC-SHA256.")
    .Validate(o => o.AccessTokenLifetimeMinutes > 0, "Jwt:AccessTokenLifetimeMinutes must be greater than zero.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Validation settings are taken from the same bound JwtOptions the token
// service signs with, rather than from a snapshot read straight off the
// configuration here. Reading it eagerly meant the two could disagree: any
// source layered on after this line — User Secrets, an integration test's own
// values — reached the signing side through IOptions but never the validating
// side, and every token the service issued came back 401 against its own keys.
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
            RoleClaimType = JwtTokenService.RoleClaimType,
            NameClaimType = "name"
        };
    });

// Deny by default: an endpoint that declares nothing still demands a signed-in
// caller, so a controller added later cannot end up open to the world just
// because someone forgot the attribute. Everything anonymous — registration,
// login, the health probe — says so explicitly with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "BuildNexus User Service", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/auth/login."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

// Local development only: guarantees an Admin exists to log in with, since
// self-service registration refuses that role. Real environments get their
// first Admin from a secret or manual creation after deploy (US-35).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<AdminSeeder>();
}

var app = builder.Build();

// Bring the schema up to date before anything reads or writes, including the
// AdminSeeder below. A failure here stops the service rather than letting it
// serve requests against a schema it does not match.
DatabaseMigrator.Migrate(
    app.Configuration.GetConnectionString("UserDb")
        ?? throw new InvalidOperationException("Connection string 'UserDb' is not configured."),
    app.Services.GetRequiredService<ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "user-service", status = "healthy" }))
   .AllowAnonymous();

app.MapControllers();

app.Run();

// Exposed so the integration tests can boot the real application host.
public partial class Program { }
