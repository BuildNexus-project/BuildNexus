using BuildNexus.UserService.Data;
using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Services;

/// <summary>
/// Creates a single bootstrap Admin account at startup when none exists, so the
/// system is not permanently adminless — self-service registration deliberately
/// refuses the Admin role.
/// </summary>
/// <remarks>
/// <para>
/// The password is hashed here by the same <see cref="IPasswordHasher"/> the
/// registration endpoint uses, and inserted through the same repository call.
/// Nothing about the hash is hard-coded, so raising the work factor or changing
/// the algorithm cannot leave this account unable to log in.
/// </para>
/// <para>
/// <b>Local development only</b> — it is registered for the Development
/// environment alone. When US-35 provisions real environments, the first Admin
/// must come from a real secret or be created manually after deploy; this
/// account's password is public knowledge in the repository.
/// </para>
/// </remarks>
public class AdminSeeder : IHostedService
{
    /// <summary>Fixed, so re-seeding on any machine produces the same row.</summary>
    private static readonly Guid BootstrapAdminId = Guid.Parse("00000000-0000-0000-0000-00000000ad11");

    private const string BootstrapFullName = "System Administrator";
    private const string BootstrapEmail = "admin@buildnexus.local";
    private const string BootstrapPassword = "ChangeMe123!";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(
        IServiceScopeFactory scopeFactory,
        IPasswordHasher passwordHasher,
        ILogger<AdminSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        if (await userRepository.AdminExistsAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var admin = new User
        {
            Id = BootstrapAdminId,
            FullName = BootstrapFullName,
            Email = BootstrapEmail,
            PasswordHash = _passwordHasher.Hash(BootstrapPassword),
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await userRepository.InsertAsync(admin);
        }
        catch (DuplicateEmailException)
        {
            // Another instance seeded it first; nothing to do.
            return;
        }

        _logger.LogWarning(
            "Seeded bootstrap Admin {Email} with the development password. Change it before this reaches any shared environment.",
            BootstrapEmail);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
