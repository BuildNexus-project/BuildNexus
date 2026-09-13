using BuildNexus.UserService.Services;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The algorithm behind every stored password: hashing on registration, and
/// verifying on login and password reset. No database needed — this is pure
/// cryptography over strings.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_accepts_the_password_that_was_hashed()
    {
        var hash = _hasher.Hash("CorrectHorse123");

        Assert.True(_hasher.Verify("CorrectHorse123", hash));
    }

    [Fact]
    public void Verify_refuses_a_different_password()
    {
        var hash = _hasher.Hash("CorrectHorse123");

        Assert.False(_hasher.Verify("WrongPassword456", hash));
    }

    [Fact]
    public void Verify_is_case_sensitive()
    {
        var hash = _hasher.Hash("CorrectHorse123");

        Assert.False(_hasher.Verify("correcthorse123", hash));
    }

    [Fact]
    public void Hash_salts_so_two_hashes_of_the_same_password_differ()
    {
        // A per-user random salt is the whole point: without it, two accounts
        // sharing a password would show it in the stored value.
        var first = _hasher.Hash("CorrectHorse123");
        var second = _hasher.Hash("CorrectHorse123");

        Assert.NotEqual(first, second);

        // Different bytes, same secret — both still verify.
        Assert.True(_hasher.Verify("CorrectHorse123", first));
        Assert.True(_hasher.Verify("CorrectHorse123", second));
    }

    [Fact]
    public void Hash_is_self_describing_so_the_work_factor_can_change_later()
    {
        var hash = _hasher.Hash("CorrectHorse123");

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("210000", parts[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-pbkdf2-hash-at-all")]
    [InlineData("pbkdf2-sha256$not-a-number$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$210000$not-base64!!!$aGFzaA==")]
    [InlineData("bcrypt$210000$c2FsdA==$aGFzaA==")]
    public void Verify_refuses_anything_that_is_not_a_well_formed_hash(string storedValue)
    {
        Assert.False(_hasher.Verify("CorrectHorse123", storedValue));
    }

    [Fact]
    public void Verify_refuses_an_empty_password()
    {
        var hash = _hasher.Hash("CorrectHorse123");

        Assert.False(_hasher.Verify("", hash));
    }

    [Fact]
    public void Verify_refuses_a_hash_whose_bytes_were_tampered_with()
    {
        var hash = _hasher.Hash("CorrectHorse123");
        var parts = hash.Split('$');

        // Flip one character of the stored hash, keeping it well-formed base64
        // of the same length — a mismatch FixedTimeEquals must catch, not a
        // format refusal (that path is covered above).
        var hashChars = parts[3].ToCharArray();
        hashChars[0] = hashChars[0] == 'A' ? 'B' : 'A';
        var tampered = string.Join('$', parts[0], parts[1], parts[2], new string(hashChars));

        Assert.False(_hasher.Verify("CorrectHorse123", tampered));
    }

    [Fact]
    public void Hash_throws_for_a_null_password()
    {
        Assert.Throws<ArgumentNullException>(() => _hasher.Hash(null!));
    }
}
