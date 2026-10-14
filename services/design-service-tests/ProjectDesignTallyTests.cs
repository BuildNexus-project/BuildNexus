using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// How a project's several documents, each at its own point in review, are read
/// as one design status on the Client's dashboard (US-21 AC-1). Pure logic — no
/// database — so the priority between the states is pinned here, and the counts
/// it reads are what <c>DesignDashboardRepositoryDatabaseTests</c> proves.
/// </summary>
public class ProjectDesignTallyTests
{
    [Fact]
    public void A_project_with_no_documents_has_no_design()
    {
        Assert.Equal(ProjectDesignState.NoDesign, ProjectDesignTally.Empty(Guid.NewGuid()).State);
    }

    [Fact]
    public void An_empty_tally_belongs_to_the_project_it_was_built_for()
    {
        var projectId = Guid.NewGuid();

        Assert.Equal(projectId, ProjectDesignTally.Empty(projectId).ProjectId);
    }

    [Theory]
    [InlineData(1, 0, 0, ProjectDesignState.AwaitingReview)]
    [InlineData(0, 1, 0, ProjectDesignState.RevisionRequested)]
    [InlineData(0, 0, 1, ProjectDesignState.Approved)]
    public void A_single_document_is_in_the_state_of_its_latest_version(
        int awaiting, int revision, int approved, ProjectDesignState expected)
    {
        Assert.Equal(expected, Tally(awaiting, revision, approved).State);
    }

    [Fact]
    public void Something_waiting_on_the_client_outranks_everything_else()
    {
        Assert.Equal(ProjectDesignState.AwaitingReview, Tally(awaiting: 1, revision: 2, approved: 3).State);
    }

    [Fact]
    public void A_revision_the_architect_owes_outranks_documents_already_approved()
    {
        Assert.Equal(ProjectDesignState.RevisionRequested, Tally(awaiting: 0, revision: 1, approved: 4).State);
    }

    [Fact]
    public void A_project_is_only_approved_when_every_document_is()
    {
        Assert.Equal(ProjectDesignState.Approved, Tally(awaiting: 0, revision: 0, approved: 3).State);
    }

    private static ProjectDesignTally Tally(int awaiting, int revision, int approved) => new()
    {
        ProjectId = Guid.NewGuid(),
        DocumentCount = awaiting + revision + approved,
        AwaitingReviewCount = awaiting,
        RevisionRequestedCount = revision,
        ApprovedCount = approved
    };
}
