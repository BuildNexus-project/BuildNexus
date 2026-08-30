using System.ComponentModel.DataAnnotations;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// What a status change request is held to before the action is entered. Pure
/// validation, so these need neither MySQL nor a broker.
/// </summary>
/// <remarks>
/// This layer only asks "is that a status at all". Whether the project may
/// actually move there depends on the status it currently holds, which is not
/// knowable until the row has been read — that is
/// <see cref="ProjectStatusTransitions"/>'s job, covered separately.
/// </remarks>
public class UpdateProjectStatusRequestTests
{
    [Theory]
    [InlineData("Pending")]
    [InlineData("Designing")]
    [InlineData("DesignApproved")]
    [InlineData("Construction")]
    [InlineData("Completed")]
    public void Accepts_every_real_status_name(string status)
    {
        Assert.Empty(Validate(new UpdateProjectStatusRequest { Status = status }));
    }

    [Fact]
    public void Accepts_every_name_the_enum_actually_has()
    {
        // Written over the enum as well as over the five names above, so a
        // status added in a later story is covered here without anyone
        // remembering to come back.
        foreach (var status in Enum.GetNames<ProjectStatus>())
        {
            Assert.Empty(Validate(new UpdateProjectStatusRequest { Status = status }));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_status(string blank)
    {
        Assert.Contains(
            Validate(new UpdateProjectStatusRequest { Status = blank }),
            result => result.MemberNames.Contains("Status"));
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("design approved")]
    [InlineData("Approved")]
    public void Rejects_something_that_is_not_a_status(string status)
    {
        // A word that is not in the enum cannot reach the transition check, and
        // it must not reach the database either — ck_projects_status would
        // refuse it as a constraint violation nobody can read.
        Assert.Contains(
            Validate(new UpdateProjectStatusRequest { Status = status }),
            result => result.MemberNames.Contains("Status"));
    }

    private static IReadOnlyList<ValidationResult> Validate(UpdateProjectStatusRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results;
    }
}
