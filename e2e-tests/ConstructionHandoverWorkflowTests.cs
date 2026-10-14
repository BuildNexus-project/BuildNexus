namespace BuildNexus.EndToEnd.Tests;

/// <summary>
/// US-31, second flow: from an approved design through the build to handover — the part of the
/// project's life the first flow stops short of.
/// </summary>
/// <remarks>
/// Tagged <c>E2E</c> only, like <see cref="FullProjectWorkflowTests"/>; see there for how to run it.
/// <para>
/// What makes this flow worth a browser is how much of it only works because services told each
/// other things over Kafka: the Construction Service has to hear the design was approved before it
/// offers milestones; starting construction makes the Payment Service raise the invoice by itself;
/// and handover is refused until the Payment Service has announced the final payment settled.
/// None of those has a screen that shows the event arriving — the test waits for the effect.
/// </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ConstructionHandoverWorkflowTests
{
    [Fact]
    public async Task Project_manager_builds_the_project_the_client_pays_and_it_is_handed_over()
    {
        await WorkflowSetup.RunAsync((app, run, designFile) =>
        {
            const string quotedTotal = "25000000";

            // Register, create the project, staff it, upload the design, approve it.
            var project = WorkflowSetup.RegisterAndApproveDesign(app, run, designFile);

            // The company moves the project on once the client has approved the design. Approving
            // the document does not do it: the status is the company's word, not the client's.
            app.SignIn(project.Architect);
            app.MoveProjectTo(project.ProjectId, "Design Approved");
            app.SignOut();

            // The Project Manager quotes the project, builds the standard milestones and starts
            // construction. The quotation matters: the invoice raised on start is for its total.
            app.SignIn(project.ProjectManager);
            app.Quote(project.ProjectId, quotedTotal);
            app.CreateMilestonesFromTemplate(project.ProjectId);
            app.StartConstruction();
            app.SignOut();

            // Nobody raised an invoice, yet the Client has one: the Payment Service made it from the
            // ConstructionStarted event. The Client pays it in full.
            app.SignIn(project.Client);
            app.PayInvoice(project.ProjectName, quotedTotal);
            app.SignOut();

            // The Project Manager finishes the build and hands it over. Handover waits on the
            // Payment Service's settlement event, so it is retried until it is accepted.
            app.SignIn(project.ProjectManager);
            app.OpenProject(project.ProjectId);
            app.CompleteAllMilestones();
            app.CompleteConstruction();
            app.HandOver(project.ProjectId);
        });
    }
}
