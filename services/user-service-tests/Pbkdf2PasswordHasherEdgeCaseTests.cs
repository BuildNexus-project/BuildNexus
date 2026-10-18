using BuildNexus.UserService.Services;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// Stored hashes that are almost right. Each would get a caller signed in if one
/// of <see cref="Pbkdf2PasswordHasher.Verify"/>'s checks were weakened.
/// </summary>
/// <remarks>
/// Added for mutation testing (SE3112-04). <see cref="Pbkdf2PasswordHasherTests"/>
/// feeds Verify hashes that are wrong in several ways at once, so each fails
/// whichever check catches it first. Stryker showed that turning an <c>||</c> into
/// an <c>&amp;&amp;</c>, or a <c>&lt;=</c> into a <c>&lt;</c>, still passed. Each
/// input here is wrong in exactly one way, so only that one check can refuse it.
/// </remarks>
public class Pbkdf2PasswordHasherEdgeCaseTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void An_empty_password_is_refused_even_against_a_hash_of_the_empty_string()
    {
        // The hash really is of "", so only the empty-password check can refuse it.
        var hashOfEmpty = _hasher.Hash("");

        Assert.False(_hasher.Verify("", hashOfEmpty));
    }

    [Fact]
    public void A_correct_hash_under_another_algorithms_prefix_is_refused()
    {
        // Same salt and bytes as a genuine hash; only the prefix is wrong.
        var parts = _hasher.Hash("CorrectHorse123").Split('$');
        var relabelled = string.Join('$', "bcrypt", parts[1], parts[2], parts[3]);

        Assert.False(_hasher.Verify("CorrectHorse123", relabelled));
    }

    [Fact]
    public void A_hash_claiming_zero_iterations_is_refused_rather_than_throwing()
    {
        // PBKDF2 with 0 iterations throws. Verify has to answer "no", not crash the login.
        var parts = _hasher.Hash("CorrectHorse123").Split('$');
        var zeroIterations = string.Join('$', parts[0], "0", parts[2], parts[3]);

        Assert.False(_hasher.Verify("CorrectHorse123", zeroIterations));
    }
}
