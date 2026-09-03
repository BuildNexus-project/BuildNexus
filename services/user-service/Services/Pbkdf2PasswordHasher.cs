using System.Security.Cryptography;

namespace BuildNexus.UserService.Services;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hasher with a per-user random salt.
/// </summary>
/// <remarks>
/// The stored value is self-describing so the work factor can be raised later
/// without invalidating existing hashes:
/// <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>.
/// </remarks>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return string.Join('$',
            Prefix,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
