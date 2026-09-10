using System.Text;
using BuildNexus.DesignService.Authorization;
using BuildNexus.DesignService.Configuration;
using BuildNexus.DesignService.Data;
using BuildNexus.DesignService.Messaging;
using BuildNexus.DesignService.Projects;
using BuildNexus.DesignService.Services;
using BuildNexus.DesignService.Users;
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
builder.Services.AddScoped<IDesignDocumentRepository, DesignDocumentRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();

// One producer for the process, held open. Building a Kafka producer starts
// background threads and a connection pool, so one per request would spend more
// on setup than on the publish itself.
builder.Services.AddSingleton<IDesignEventPublisher, KafkaDesignEventPublisher>();

// The other half of a reliable publish: the endpoints record events inside the
// transaction that made the change, and this drains them onto the topic
// afterwards. Nothing on the request path waits for the broker.
builder.Services.AddHostedService<OutboxDispatcher>();

// The Project Service, asked over HTTP — with the caller's own token — whether
// a caller may touch a project. Its address is validated at startup for the
// same reason the JWT settings are: a service that cannot reach it would refuse
// every upload at runtime, and a log line after the first one is too late.
builder.Services.AddOptions<ProjectServiceOptions>()
    .Bind(builder.Configuration.GetSection(ProjectServiceOptions.SectionName))
    .Validate(
        o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
        "Services:ProjectService:BaseUrl must be an absolute URL.")
    .Validate(o => o.TimeoutSeconds > 0, "Services:ProjectService:TimeoutSeconds must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IProjectAccessClient, HttpProjectAccessClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ProjectServiceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

// The User Service, asked over HTTP for a name and email to notify — see
// HttpInternalUserClient. Same validation reasoning as ProjectServiceOptions.
builder.Services.AddOptions<UserServiceOptions>()
    .Bind(builder.Configuration.GetSection(UserServiceOptions.SectionName))
    .Validate(
        o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
        "Services:UserService:BaseUrl must be an absolute URL.")
    .Validate(o => o.TimeoutSeconds > 0, "Services:UserService:TimeoutSeconds must be greater than zero.")
    .ValidateOnStart();

// The shared secret this service presents to /api/internal on the User
// Service. Validated at startup at the same 32-byte bar as the JWT signing key
// — see user-service's identically-named options, which check the same value
// on the receiving side.
builder.Services.AddOptions<InternalServiceOptions>()
    .Bind(builder.Configuration.GetSection(InternalServiceOptions.SectionName))
    .Validate(
        o => Encoding.UTF8.GetByteCount(o.ApiKey) >= InternalServiceOptions.MinimumApiKeyBytes,
        $"InternalService:ApiKey must be at least {InternalServiceOptions.MinimumApiKeyBytes} bytes.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IInternalUserClient, HttpInternalUserClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<UserServiceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

// Revision-request delivery. Both senders are registered; which one answers
// IEmailSender is decided when it is resolved, from the options as they finally
// stand — the same registration user-service uses for its own password-reset
// email.
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<LoggingEmailSender>();
builder.Services.AddSingleton<IEmailSender>(provider =>
    string.IsNullOrWhiteSpace(provider.GetRequiredService<IOptions<EmailOptions>>().Value.SmtpHost)
        ? provider.GetRequiredService<LoggingEmailSender>()
        : provider.GetRequiredService<SmtpEmailSender>());

// Scoped, not Singleton: it depends on IInternalUserClient, a typed HttpClient,
// which AddHttpClient registers as Transient — a Singleton holding that
// indefinitely is exactly the captive-dependency problem IHttpClientFactory
// exists to avoid.
builder.Services.AddScoped<IRevisionRequestNotifier, RevisionRequestNotifier>();

builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.FromAddress), "Email:FromAddress must be configured.")
    .Validate(o => o.SmtpPort > 0, "Email:SmtpPort must be greater than zero.")
    .ValidateOnStart();

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
// that out from a log line after the first approval is too late.
builder.Services.AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers), "Kafka:BootstrapServers must be configured.")
    .Validate(o => o.MessageTimeoutMs > 0, "Kafka:MessageTimeoutMs must be greater than zero.")
    .ValidateOnStart();

// Dispatcher tuning. Both settings have working defaults, unlike the broker
// address, so this only guards against a deployment configuring them to
// something that cannot work.
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .Validate(o => o.PollIntervalSeconds > 0, "Outbox:PollIntervalSeconds must be greater than zero.")
    .Validate(o => o.BatchSize > 0, "Outbox:BatchSize must be greater than zero.")
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
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "BuildNexus Design Service", Version = "v1" });

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
    app.Configuration.GetConnectionString("DesignDb")
        ?? throw new InvalidOperationException("Connection string 'DesignDb' is not configured."),
    app.Services.GetRequiredService<ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "design-service", status = "healthy" }))
   .AllowAnonymous();

app.MapControllers();

app.Run();

// Exposed so the integration tests can boot the real application host.
public partial class Program { }
