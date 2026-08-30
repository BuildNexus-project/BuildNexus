using System.ComponentModel.DataAnnotations;
using BuildNexus.ProjectService.Contracts;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The rules a submitted project is held to before anything reaches the
/// database. Pure validation, so these need neither MySQL nor a broker.
/// </summary>
public class CreateProjectRequestTests
{
    [Fact]
    public void Accepts_a_fully_filled_in_form()
    {
        Assert.Empty(Validate(Request()));
    }

    [Fact]
    public void Accepts_a_form_with_no_other_requirements()
    {
        // The one optional field on the form: a Client with nothing to add must
        // still be able to submit.
        Assert.Empty(Validate(Request(otherRequirements: null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepts_blank_other_requirements(string blank)
    {
        Assert.Empty(Validate(Request(otherRequirements: blank)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Rejects_a_name_that_is_missing_or_too_short(string name)
    {
        Assert.Contains(Validate(Request(name: name)), result => result.MemberNames.Contains("Name"));
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_column_holds()
    {
        Assert.Contains(
            Validate(Request(name: new string('a', 151))),
            result => result.MemberNames.Contains("Name"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void Rejects_a_location_that_is_missing_or_too_short(string location)
    {
        Assert.Contains(Validate(Request(location: location)), result => result.MemberNames.Contains("Location"));
    }

    [Fact]
    public void Rejects_a_location_longer_than_the_column_holds()
    {
        Assert.Contains(
            Validate(Request(location: new string('a', 256))),
            result => result.MemberNames.Contains("Location"));
    }

    [Fact]
    public void Rejects_a_form_with_no_land_size()
    {
        // Nullable rather than a plain decimal precisely so this is a missing
        // value rather than a silent zero.
        Assert.Contains(
            Validate(Request(landSizePerches: null)),
            result => result.MemberNames.Contains("LandSizePerches"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_land_size_that_is_not_a_real_plot(decimal landSize)
    {
        Assert.Contains(
            Validate(Request(landSizePerches: landSize)),
            result => result.MemberNames.Contains("LandSizePerches"));
    }

    [Fact]
    public void Accepts_a_fractional_land_size()
    {
        // Perches are bought and sold in fractions, so this must not become an
        // integer field by the back door.
        Assert.Empty(Validate(Request(landSizePerches: 25.75m)));
    }

    [Fact]
    public void Rejects_a_land_size_larger_than_the_column_holds()
    {
        // DECIMAL(10,2) tops out at 99999999.99; anything past it is refused
        // here rather than overflowing the column.
        Assert.Contains(
            Validate(Request(landSizePerches: 100_000_000m)),
            result => result.MemberNames.Contains("LandSizePerches"));
    }

    [Fact]
    public void Rejects_a_form_with_no_budget()
    {
        Assert.Contains(Validate(Request(budget: null)), result => result.MemberNames.Contains("Budget"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void Rejects_a_budget_that_is_not_money(decimal budget)
    {
        Assert.Contains(Validate(Request(budget: budget)), result => result.MemberNames.Contains("Budget"));
    }

    [Fact]
    public void Rejects_a_budget_larger_than_the_column_holds()
    {
        // DECIMAL(15,2) tops out at 9999999999999.99.
        Assert.Contains(
            Validate(Request(budget: 10_000_000_000_000m)),
            result => result.MemberNames.Contains("Budget"));
    }

    [Fact]
    public void Rejects_a_form_with_no_floor_count()
    {
        Assert.Contains(Validate(Request(floors: null)), result => result.MemberNames.Contains("Floors"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_a_floor_count_that_is_not_a_building(int floors)
    {
        // Zero floors is not a building, which is why this one field does not
        // accept the zero its neighbours do.
        Assert.Contains(Validate(Request(floors: floors)), result => result.MemberNames.Contains("Floors"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Accepts_a_bedroom_count_anywhere_in_range(int bedrooms)
    {
        // Zero is a legitimate answer — not every build is a house.
        Assert.Empty(Validate(Request(bedrooms: bedrooms)));
    }

    [Fact]
    public void Rejects_a_form_with_no_bedroom_count()
    {
        // The distinction nullable buys us: "none" and "not answered" are
        // different, and only one of them is allowed through.
        Assert.Contains(Validate(Request(bedrooms: null)), result => result.MemberNames.Contains("Bedrooms"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_a_bedroom_count_outside_range(int bedrooms)
    {
        Assert.Contains(Validate(Request(bedrooms: bedrooms)), result => result.MemberNames.Contains("Bedrooms"));
    }

    [Fact]
    public void Rejects_a_form_with_no_bathroom_count()
    {
        Assert.Contains(Validate(Request(bathrooms: null)), result => result.MemberNames.Contains("Bathrooms"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_a_bathroom_count_outside_range(int bathrooms)
    {
        Assert.Contains(Validate(Request(bathrooms: bathrooms)), result => result.MemberNames.Contains("Bathrooms"));
    }

    [Fact]
    public void Rejects_a_form_with_no_garage_count()
    {
        Assert.Contains(
            Validate(Request(garageSpaces: null)),
            result => result.MemberNames.Contains("GarageSpaces"));
    }

    [Fact]
    public void Accepts_no_garage_as_zero_spaces()
    {
        // "No garage" and "two cars" are the same question, answered with a
        // number rather than a checkbox.
        Assert.Empty(Validate(Request(garageSpaces: 0)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Rejects_a_garage_count_outside_range(int garageSpaces)
    {
        Assert.Contains(
            Validate(Request(garageSpaces: garageSpaces)),
            result => result.MemberNames.Contains("GarageSpaces"));
    }

    [Fact]
    public void Rejects_other_requirements_longer_than_the_form_allows()
    {
        Assert.Contains(
            Validate(Request(otherRequirements: new string('a', 2001))),
            result => result.MemberNames.Contains("OtherRequirements"));
    }

    [Fact]
    public void Accepts_other_requirements_right_up_to_the_limit()
    {
        Assert.Empty(Validate(Request(otherRequirements: new string('a', 2000))));
    }

    private static CreateProjectRequest Request(
        string name = "Beachfront villa",
        string location = "Galle",
        decimal? landSizePerches = 25.5m,
        decimal? budget = 18_500_000m,
        int? floors = 2,
        int? bedrooms = 4,
        int? bathrooms = 3,
        int? garageSpaces = 2,
        string? otherRequirements = "Solar hot water") => new()
        {
            Name = name,
            Location = location,
            LandSizePerches = landSizePerches,
            Budget = budget,
            Floors = floors,
            Bedrooms = bedrooms,
            Bathrooms = bathrooms,
            GarageSpaces = garageSpaces,
            OtherRequirements = otherRequirements
        };

    /// <summary>
    /// Runs the request through the same validator ASP.NET Core applies before
    /// the action method is entered.
    /// </summary>
    private static List<ValidationResult> Validate(CreateProjectRequest request)
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
