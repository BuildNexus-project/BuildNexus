using System.ComponentModel.DataAnnotations;
using BuildNexus.DesignService.Contracts;

namespace BuildNexus.DesignService.Tests;

/// <summary>The form-level rule on a revision request: the comment, unlike an upload's, is required.</summary>
public class RequestRevisionRequestTests
{
    [Fact]
    public void Accepts_a_comment()
    {
        Assert.Empty(Validate(Request("Move the stairs to the east wall.")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_comment(string comment)
    {
        Assert.Contains(
            Validate(Request(comment)), r => r.MemberNames.Contains(nameof(RequestRevisionRequest.Comment)));
    }

    [Fact]
    public void Rejects_a_comment_over_two_thousand_characters()
    {
        Assert.Contains(
            Validate(Request(new string('x', 2001))),
            r => r.MemberNames.Contains(nameof(RequestRevisionRequest.Comment)));
    }

    [Fact]
    public void Accepts_a_comment_at_the_two_thousand_character_limit()
    {
        Assert.Empty(Validate(Request(new string('x', 2000))));
    }

    private static RequestRevisionRequest Request(string comment) => new() { Comment = comment };

    private static IReadOnlyList<ValidationResult> Validate(RequestRevisionRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
