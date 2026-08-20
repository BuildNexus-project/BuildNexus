using System.Text;
using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Data;
using BuildNexus.UserService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Data access (ADO.NET, direct SQL — no ORM)
builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// Security services
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { service = "user-service", status = "healthy" }));

app.MapControllers();

app.Run();
