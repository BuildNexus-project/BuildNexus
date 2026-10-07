namespace BuildNexus.EndToEnd.Tests;

/// <summary>A project whose design has been uploaded and approved, and the people around it.</summary>
public sealed record ApprovedProject(
    Account Client,
    Account Architect,
    Account ProjectManager,
    Guid ProjectId,
    string ProjectName,
    string DocumentName);

/// <summary>
/// The opening of every flow: three people register, the Client submits a project, the Admin
/// staffs it, the Architect uploads a design and the Client approves it.
/// </summary>
/// <remarks>
/// Shared so each flow starts from the same, already-verified ground and only spells out what is
/// its own. Leaves the browser signed out. Every name carries the run's suffix, so repeated runs
/// against the same databases never collide.
/// </remarks>
public static class WorkflowSetup
{
    /// <summary>
    /// Runs one flow in its own Chrome window: checks the stack is up, gives the flow a unique run
    /// suffix and a design file to upload, and saves a screenshot of the failing page if it throws.
    /// </summary>
    public static async Task RunAsync(Action<BuildNexusApp, string, string> flow)
    {
        await StackPreflight.EnsureFrontendIsUpAsync();

        var run = Guid.NewGuid().ToString("N")[..8];
        var designFile = TestFiles.WriteSamplePng(run);

        using var browser = new BrowserSession();

        try
        {
            flow(new BuildNexusApp(browser), run, designFile);
        }
        catch (Exception)
        {
            // The page's text is already in the failure message; the picture is for what text can't
            // say. Saved beside the test binaries — CI uploads that folder.
            var screenshot = browser.Screenshot($"failure-{run}");

            if (screenshot is not null)
            {
                Console.Error.WriteLine($"Screenshot of the failing page: {screenshot}");
            }

            throw;
        }
        finally
        {
            File.Delete(designFile);
        }
    }

    public static ApprovedProject RegisterAndApproveDesign(BuildNexusApp app, string run, string designFile)
    {
        var projectName = $"E2E House {run}";
        var documentName = $"FloorPlan {run}";

        // Register: three people sign up through the real form. An Admin cannot, which is why the
        // seeded one is used below.
        var client = app.Register($"E2E Client {run}", $"e2e.client.{run}@buildnexus.test", "Client", "Client");
        var architect = app.Register($"E2E Architect {run}", $"e2e.architect.{run}@buildnexus.test", "Architect", "Architect");
        var projectManager = app.Register($"E2E Manager {run}", $"e2e.pm.{run}@buildnexus.test", "ProjectManager", "Project Manager");

        // The Client submits a project.
        app.SignIn(client);
        var projectId = app.CreateProject(projectName, "Colombo 07");
        app.SignOut();

        // The Admin staffs it.
        app.SignIn(E2ESettings.AdminEmail, E2ESettings.AdminPassword);
        app.AssignStaff(projectId, architect, projectManager);
        app.SignOut();

        // The Architect uploads a design.
        app.SignIn(architect);
        app.UploadDesign(projectId, documentName, designFile);
        app.SignOut();

        // The Client approves it, and is told so — the notification only exists if the Design
        // Service's event reached the Project Service.
        app.SignIn(client);
        app.ApproveDesign(projectId, documentName);
        app.WaitForNotification($"was approved on \"{projectName}\"");
        app.SignOut();

        return new ApprovedProject(client, architect, projectManager, projectId, projectName, documentName);
    }
}
