using System.ComponentModel.DataAnnotations;
using BuildNexus.UserService.Contracts;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The rules the two password reset payloads are held to before anything
/// reaches the database. Pure validation, so these need no database.
/// </summary>
public class PasswordResetRequestTests
{
    [Fact]
    public void A_reset_request_needs_a_well_formed_address()
    {
        Assert.Empty(Validate(new ForgotPasswordRequest { Email = "ada@example.com" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void A_reset_request_is_refused_without_one(string email)
    {
        Assert.Contains(
            Validate(new ForgotPasswordRequest { Email = email }),
            result => result.MemberNames.Contains(nameof(ForgotPasswordRequest.Email)));
    }

    [Fact]
    public void A_reset_accepts_a_token_and_a_password_that_meets_the_signup_rules()
    {
        Assert.Empty(Validate(ResetRequest()));
    }

    [Fact]
    public void A_reset_is_refused_without_a_token()
    {
        Assert.Contains(
            Validate(ResetRequest(token: "")),
            result => result.MemberNames.Contains(nameof(ResetPasswordRequest.Token)));
    }

    [Fact]
    public void A_reset_is_refused_when_the_token_is_longer_than_any_this_service_mints()
    {
        // Bounded so a caller cannot hand the service a megabyte to hash on an
        // endpoint that needs no credential to reach.
        Assert.Contains(
            Validate(ResetRequest(token: new string('t', 257))),
            result => result.MemberNames.Contains(nameof(ResetPasswordRequest.Token)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sh0rt")]
    [InlineData("nodigitsatall")]
    [InlineData("12345678")]
    public void A_reset_is_refused_when_the_new_password_is_weaker_than_signup_allows(string password)
    {
        // The same rules registration applies. A password chosen through a reset
        // must never be one the sign-up form would have turned away.
        Assert.Contains(
            Validate(ResetRequest(newPassword: password)),
            result => result.MemberNames.Contains(nameof(ResetPasswordRequest.NewPassword)));
    }

    private static ResetPasswordRequest ResetRequest(
        string token = "a-token-long-enough-to-pass",
        string newPassword = "NewPassw0rd") =>
        new() { Token = token, NewPassword = newPassword };

    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results;
    }
}
