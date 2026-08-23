using System.ComponentModel.DataAnnotations;
using BuildNexus.UserService.Contracts;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The rules a profile update is held to before anything reaches the database.
/// Pure validation, so these run without the development database.
/// </summary>
public class UpdateProfileRequestTests
{
    [Fact]
    public void Accepts_a_name_with_contact_details()
    {
        var request = Request(phoneNumber: "+94 77 123 4567", contactAddress: "12 Galle Road, Colombo 03");

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Accepts_a_name_on_its_own()
    {
        // Contact details are optional: an account that has never filled them in
        // must still be able to correct its name.
        Assert.Empty(Validate(Request()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepts_blank_contact_details_so_the_form_can_clear_them(string blank)
    {
        Assert.Empty(Validate(Request(phoneNumber: blank, contactAddress: blank)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void Rejects_a_name_that_is_missing_or_too_short(string fullName)
    {
        Assert.Contains(Validate(Request(fullName)), result => result.MemberNames.Contains("FullName"));
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_column_holds()
    {
        var request = Request(new string('a', 151));

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("FullName"));
    }

    [Theory]
    [InlineData("0771234567")]
    [InlineData("+94771234567")]
    [InlineData("(011) 234-5678")]
    [InlineData("011 234 5678")]
    public void Accepts_the_usual_ways_of_writing_a_number(string phoneNumber)
    {
        Assert.Empty(Validate(Request(phoneNumber: phoneNumber)));
    }

    [Theory]
    [InlineData("call me")]          // not a number at all
    [InlineData("12345")]            // too few digits to be one
    [InlineData("+")]                // a country code prefix and nothing else
    [InlineData("077-123-4567 ext 8")] // letters are not separators
    [InlineData("07712345678901234567890123456789")] // past the 30-character column
    public void Rejects_something_that_is_not_a_usable_number(string phoneNumber)
    {
        Assert.Contains(Validate(Request(phoneNumber: phoneNumber)), result => result.MemberNames.Contains("PhoneNumber"));
    }

    [Fact]
    public void Rejects_an_address_longer_than_the_column_holds()
    {
        var request = Request(contactAddress: new string('a', 256));

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("ContactAddress"));
    }

    [Theory]
    [InlineData("someone.else@example.com")]
    [InlineData("")]
    public void Rejects_an_attempt_to_change_the_email(string email)
    {
        // Refused by name rather than quietly dropped, so the caller cannot walk
        // away believing their login address moved.
        var request = Request();
        request.Email = email;

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("Email"));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Client")]
    public void Rejects_an_attempt_to_change_the_role(string role)
    {
        // Even a role matching the one already held is refused: one rule to
        // explain beats "sometimes it works".
        var request = Request();
        request.Role = role;

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("Role"));
    }

    private static UpdateProfileRequest Request(
        string fullName = "Ada Perera",
        string? phoneNumber = null,
        string? contactAddress = null) => new()
        {
            FullName = fullName,
            PhoneNumber = phoneNumber,
            ContactAddress = contactAddress
        };

    /// <summary>
    /// Runs the request through the same validator ASP.NET Core applies before
    /// the action method is entered.
    /// </summary>
    private static List<ValidationResult> Validate(UpdateProfileRequest request)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        return results;
    }
}
