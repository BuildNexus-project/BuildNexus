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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { service = "user-service", status = "healthy" }));

app.MapControllers();

app.Run();
