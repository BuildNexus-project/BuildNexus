using System.Text;
using BuildNexus.PaymentService.Authorization;
using BuildNexus.PaymentService.Configuration;
using BuildNexus.PaymentService.Data;
using BuildNexus.PaymentService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// System.Text.Json is configured once here so every controller reads and writes
// the same shape: InvoiceStatus round-trips as its own name ("Pending" / "Paid")
// rather than the enum's integer ordinal — kinder to the React side, and safer,
// because inserting or reordering an enum member would silently change the wire
// value of the existing ones. allowIntegerValues: false rejects requests that
// send the integer form ({"status": 1}) — the wire contract is the string name,
// and accepting the ordinal too would recreate the hole the string form closes.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false));
    });
builder.Services.AddEndpointsApiExplorer();

// Data access (ADO.NET, direct SQL — no ORM)
builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<IQuotationRepository, QuotationRepository>();
builder.Services.AddScoped<IProjectOwnerRepository, ProjectOwnerRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();

// Broker address, validated at startup: a consumer that cannot say where Kafka
// is will read nothing, and the ownership rows the Client's quotation view is
// gated on would never arrive.
builder.Services.AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers), "Kafka:BootstrapServers must be configured.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConsumerGroupId), "Kafka:ConsumerGroupId must be configured.")
    .ValidateOnStart();

// Reads project-events and records which Client owns each project, so the
// Client-facing quotation read can be scoped to the caller's own projects.
// Nothing on any request path waits on it.
builder.Services.AddHostedService<ProjectEventsConsumer>();

// Reads construction-events and raises a project's invoice when its build
// starts (AC-2's automatic path). Nothing on any request path waits on it.
builder.Services.AddHostedService<ConstructionEventsConsumer>();

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

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Resolved per request through EventsType below, so it can take an ILogger.
builder.Services.AddScoped<AuthorizationProblemEvents>();

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
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "BuildNexus Payment Service", Version = "v1" });

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
    app.Configuration.GetConnectionString("PaymentDb")
        ?? throw new InvalidOperationException("Connection string 'PaymentDb' is not configured."),
    app.Services.GetRequiredService<ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "payment-service", status = "healthy" }))
   .AllowAnonymous();

app.MapControllers();

app.Run();

// Exposed so the integration tests can boot the real application host.
public partial class Program { }
