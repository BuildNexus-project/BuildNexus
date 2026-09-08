using System.ComponentModel.DataAnnotations;
using BuildNexus.ProjectService.Contracts;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The one rule on a cancellation payload: a reason, between 1 and 500
/// characters. Whether a run of spaces counts is settled by the controller,
/// which trims and re-checks — see <see cref="CancelProjectEndpointTests"/>.
/// </summary>
public class CancelProjectRequestTests
{
    [Fact]
    public void Accepts_a_reason()
    {
        Assert.Empty(Validate(new CancelProjectRequest { Reason = "Client secured a different plot" }));
    }

    [Fact]
    public void Requires_a_reason()
    {
        Assert.Contains(
            Validate(new CancelProjectRequest { Reason = "" }),
            result => result.MemberNames.Contains(nameof(CancelProjectRequest.Reason)));
    }

    [Fact]
    public void Accepts_a_reason_at_the_five_hundred_character_limit()
    {
        Assert.Empty(Validate(new CancelProjectRequest { Reason = new string('a', 500) }));
    }

    [Fact]
    public void Rejects_a_reason_longer_than_the_column_will_comfortably_hold()
    {
        Assert.Contains(
            Validate(new CancelProjectRequest { Reason = new string('a', 501) }),
            result => result.MemberNames.Contains(nameof(CancelProjectRequest.Reason)));
    }

    private static IReadOnlyList<ValidationResult> Validate(CancelProjectRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
