namespace BuildNexus.UserService.Services;

/// <summary>
/// Turns a plaintext password into a storable hash and checks a candidate
/// password against one. Raw passwords never leave this abstraction.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes <paramref name="password"/> for storage in <c>users.password_hash</c>.</summary>
    string Hash(string password);

    /// <summary>
    /// Returns true when <paramref name="password"/> matches <paramref name="passwordHash"/>.
    /// A malformed or unrecognised hash returns false rather than throwing.
    /// </summary>
    bool Verify(string password, string passwordHash);
}
