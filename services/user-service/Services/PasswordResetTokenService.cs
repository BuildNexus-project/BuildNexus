using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using BuildNexus.UserService.Configuration;
using Microsoft.Extensions.Options;

namespace BuildNexus.UserService.Services;

/// <summary>
/// Mints 256-bit random reset tokens and hashes them with SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// The token is 32 bytes from the cryptographic random generator, base64url
/// encoded so it survives a query string unescaped. Guessing one is not a
/// practical attack, which matters because anyone holding it can change the
/// account's password.
/// </para>
/// <para>
/// Only its hash is stored. SHA-256 rather than the PBKDF2 used for passwords,
/// and deliberately so: a password is short and human-chosen, so its hash must
/// be slow to attack, whereas this token is 256 bits of randomness with nothing
/// to guess at. What the hash buys here is that a stolen copy of the table
/// contains no working links — and the redeem path has to hash the incoming
/// token on every request, which a 210k-iteration KDF would turn into a way to
/// exhaust the service.
/// </para>
/// </remarks>
public class PasswordResetTokenService : IPasswordResetTokenService
{
    private const int TokenSizeBytes = 32;

    private readonly PasswordResetOptions _options;

    public PasswordResetTokenService(IOptions<PasswordResetOptions> options)
    {
        _options = options.Value;
    }

    public MintedResetToken Mint(DateTime issuedAtUtc)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeBytes));

        return new MintedResetToken(
            token,
            HashToken(token),
            issuedAtUtc.AddMinutes(_options.TokenLifetimeMinutes));
    }

    public string HashToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
