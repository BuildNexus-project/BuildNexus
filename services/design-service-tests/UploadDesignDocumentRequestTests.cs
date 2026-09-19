using System.ComponentModel.DataAnnotations;
using BuildNexus.DesignService.Contracts;
using Microsoft.AspNetCore.Http;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The form-level rules on an upload — the name and the revision comment. The
/// file's type and size are not here: they are checked on the bytes by
/// <see cref="DesignFileValidatorTests"/>, not by a data annotation that can
/// only see the client's label.
/// </summary>
public class UploadDesignDocumentRequestTests
{
    [Fact]
    public void Accepts_a_name_and_a_file_with_no_comment()
    {
        Assert.Empty(Validate(Request()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_name(string name)
    {
        Assert.Contains(Validate(Request(name: name)), r => r.MemberNames.Contains(nameof(UploadDesignDocumentRequest.Name)));
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_column_holds()
    {
        Assert.Contains(
            Validate(Request(name: new string('a', 151))),
            r => r.MemberNames.Contains(nameof(UploadDesignDocumentRequest.Name)));
    }

    [Fact]
    public void Accepts_a_name_at_the_hundred_and_fifty_character_limit()
    {
        Assert.Empty(Validate(Request(name: new string('a', 150))));
    }

    [Fact]
    public void Rejects_a_revision_comment_over_two_thousand_characters()
    {
        Assert.Contains(
            Validate(Request(revisionComment: new string('x', 2001))),
            r => r.MemberNames.Contains(nameof(UploadDesignDocumentRequest.RevisionComment)));
    }

    [Fact]
    public void Rejects_a_request_with_no_file()
    {
        var request = Request();
        request.File = null!;

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(UploadDesignDocumentRequest.File)));
    }

    private static UploadDesignDocumentRequest Request(
        string name = "GroundFloorPlan",
        string? revisionComment = null) => new()
        {
            Name = name,
            RevisionComment = revisionComment,
            File = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "plan.pdf")
        };

    private static IReadOnlyList<ValidationResult> Validate(UploadDesignDocumentRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
