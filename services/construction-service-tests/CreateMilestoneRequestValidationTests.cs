using System.ComponentModel.DataAnnotations;
using BuildNexus.ConstructionService.Contracts;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// The milestone-name rule on <see cref="CreateMilestoneRequest"/>: required, not
/// blank, and 1 to 150 characters (US-14).
/// </summary>
/// <remarks>
/// ASP.NET reads these attributes and fills <c>ModelState</c> before the controller
/// runs, which is why the controller tests set <c>ModelState</c> by hand. This is the
/// other half — the attributes themselves, through the same <see cref="Validator"/>
/// the framework uses. No mocks: there is nothing to isolate.
/// </remarks>
public class CreateMilestoneRequestValidationTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(149, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    [InlineData(1000, false)]
    [InlineData(0, false)]
    public void A_name_is_accepted_from_one_to_150_characters_and_refused_outside_that(
        int length,
        bool expectedValid)
    {
        var request = new CreateMilestoneRequest { Name = new string('a', length) };

        Assert.Equal(expectedValid, IsValid(request));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void A_name_made_only_of_spaces_is_refused(string name)
    {
        // So the controller's Trim() can never turn a typed name into an empty one.
        Assert.False(IsValid(new CreateMilestoneRequest { Name = name }));
    }

    /// <summary>Runs the request's validation attributes, the way the framework does.</summary>
    private static bool IsValid(object request) =>
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults: [],
            validateAllProperties: true);
}
