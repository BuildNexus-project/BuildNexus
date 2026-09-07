using System.ComponentModel.DataAnnotations;
using BuildNexus.ProjectService.Contracts;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The one rule on each assignment payload: an id is required. Whether the
/// account holds the matching role is not something a data annotation can know,
/// so that check is the User Service's — see <see cref="AssignStaffEndpointTests"/>.
/// </summary>
public class AssignStaffRequestTests
{
    [Fact]
    public void An_architect_request_needs_an_id()
    {
        Assert.Contains(
            Validate(new AssignArchitectRequest()),
            result => result.MemberNames.Contains(nameof(AssignArchitectRequest.ArchitectId)));
    }

    [Fact]
    public void An_architect_request_with_an_id_is_accepted()
    {
        Assert.Empty(Validate(new AssignArchitectRequest { ArchitectId = Guid.NewGuid() }));
    }

    [Fact]
    public void A_project_manager_request_needs_an_id()
    {
        Assert.Contains(
            Validate(new AssignProjectManagerRequest()),
            result => result.MemberNames.Contains(nameof(AssignProjectManagerRequest.ProjectManagerId)));
    }

    [Fact]
    public void A_project_manager_request_with_an_id_is_accepted()
    {
        Assert.Empty(Validate(new AssignProjectManagerRequest { ProjectManagerId = Guid.NewGuid() }));
    }

    private static IReadOnlyList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
