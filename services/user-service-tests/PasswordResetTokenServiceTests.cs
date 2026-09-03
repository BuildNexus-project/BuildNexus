using BuildNexus.UserService.Configuration;
using BuildNexus.UserService.Services;
using Microsoft.Extensions.Options;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The tokens behind a reset link: how long they last, and the hashing that
/// keeps the database from holding a working link. No database needed.
/// </summary>
public class PasswordResetTokenServiceTests
{
    private static readonly DateTime IssuedAt = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Mint_expires_the_token_the_configured_window_after_it_was_issued()
    {
        // 30 minutes is the window US-04 asks for, and it comes from
        // configuration rather than being fixed in the code.
        var minted = Service(lifetimeMinutes: 30).Mint(IssuedAt);

        Assert.Equal(IssuedAt.AddMinutes(30), minted.ExpiresAtUtc);
    }

    [Fact]
    public void Mint_honours_a_different_window()
    {
        var minted = Service(lifetimeMinutes: 5).Mint(IssuedAt);

        Assert.Equal(IssuedAt.AddMinutes(5), minted.ExpiresAtUtc);
    }

    [Fact]
    public void Mint_never_hands_back_the_same_token_twice()
    {
        var service = Service();

        var tokens = Enumerable.Range(0, 50).Select(_ => service.Mint(IssuedAt).Token).ToList();

        // A predictable token would let anyone reset anyone's password, so the
        // only acceptable number of collisions is zero.
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Mint_produces_a_token_that_survives_a_query_string_untouched()
    {
        // The token is pasted straight into the reset URL, so anything needing
        // escaping would arrive back as a different string.
        var token = Service().Mint(IssuedAt).Token;

        Assert.Equal(token, Uri.EscapeDataString(token));
    }

    [Fact]
    public void Mint_pairs_the_token_with_its_own_hash()
    {
        var service = Service();

        var minted = service.Mint(IssuedAt);

        Assert.Equal(minted.TokenHash, service.HashToken(minted.Token));
    }

    [Fact]
    public void The_stored_hash_is_not_the_token()
    {
        // What goes to the database must not be usable as a link. If these were
        // ever equal, storing the hash would be storing the token.
        var minted = Service().Mint(IssuedAt);

        Assert.NotEqual(minted.Token, minted.TokenHash);
        Assert.DoesNotContain(minted.Token, minted.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public void Hashing_the_same_token_twice_gives_the_same_answer()
    {
        // Redeeming a link works by hashing what arrived and looking it up, so
        // an unstable hash would make every link unusable.
        var service = Service();

        Assert.Equal(service.HashToken("a-token"), service.HashToken("a-token"));
    }

    [Fact]
    public void Hashing_a_different_token_gives_a_different_answer()
    {
        var service = Service();

        Assert.NotEqual(service.HashToken("a-token"), service.HashToken("a-token "));
    }

    [Fact]
    public void Hashes_are_lowercase_hex_of_a_fixed_length()
    {
        // token_hash is CHAR(64): a hash of any other shape would be truncated
        // or padded on its way into the column.
        var hash = Service().HashToken("a-token");

        Assert.Equal(64, hash.Length);
        Assert.All(hash, character => Assert.Contains(character, "0123456789abcdef"));
    }

    private static PasswordResetTokenService Service(int lifetimeMinutes = 30) =>
        new(Options.Create(new PasswordResetOptions
        {
            TokenLifetimeMinutes = lifetimeMinutes,
            ResetUrlTemplate = "http://localhost:5173/reset-password?token={token}"
        }));
}
