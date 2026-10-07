using Xunit.Sdk;

namespace BuildNexus.EndToEnd.Tests;

/// <summary>
/// US-31: the whole system, verified the way a person uses it — in a real browser, against the
/// running stack (frontend, API gateway, five services, five databases, Kafka).
/// </summary>
/// <remarks>
/// Tagged <c>E2E</c> and nothing else, so CI's unit and integration filters
/// (<c>Category!=Integration</c> / <c>Category=Integration</c>) never pick it up: it needs the
/// full stack running, which neither of those jobs starts. Run it with
/// <c>dotnet test e2e-tests/EndToEnd.Tests.csproj</c> once <c>docker compose up -d</c> and
/// <c>npm run dev</c> are running — see <c>e2e-tests/README.md</c>.
/// <para>
/// Every run registers fresh accounts under a unique suffix, so it can be repeated against the
/// same databases without colliding with what an earlier run left behind.
/// </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class FullProjectWorkflowTests
{
    /// <summary>
    /// register → create project → assign staff → upload design → approve → quote, invoice and pay,
    /// across four roles and five services.
    /// </summary>
    /// <remarks>
    /// One test, deliberately: the steps are a chain where each depends on the one before — there
    /// is no approving a design nobody uploaded — so splitting them would only make later tests
    /// fail for an earlier test's reason. Each step names what it is waiting for, so a failure
    /// points at the step that broke.
    /// <para>
    /// Two of the checks cross services through Kafka rather than through a page the user is
    /// already on: the Client's notification that the design was approved (Design Service →
    /// <c>design-events</c> → Project Service) and that a payment was received (Payment Service →
    /// <c>payment-events</c> → Project Service). They can only pass if the event actually travelled.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Client_registers_creates_a_project_and_it_is_designed_approved_and_paid_for()
    {
        await WorkflowSetup.RunAsync((app, run, designFile) =>
        {
            const string estimate = "25000000";
            const string invoiceAmount = "5000000";

            // 1-5. Register, create the project, staff it, upload the design, approve it.
            var project = WorkflowSetup.RegisterAndApproveDesign(app, run, designFile);

            // 6. The Project Manager prices the project and bills for it.
            app.SignIn(project.ProjectManager);
            app.QuoteAndInvoice(project.ProjectId, estimate, invoiceAmount);
            app.SignOut();

            // 7. The Client pays the invoice, the balance clears, and the payment event reaches the
            //    Project Service as a notification.
            app.SignIn(project.Client);
            app.PayInvoice(project.ProjectName, invoiceAmount);
            app.WaitForNotification($"A payment was received on \"{project.ProjectName}\"");
        });
    }
}

/// <summary>
/// Fails fast, with the fix in the message, when the app is not up — rather than letting Chrome
/// show "This site can't be reached" and the first wait time out 30 seconds later.
/// </summary>
internal static class StackPreflight
{
    public static async Task EnsureFrontendIsUpAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        try
        {
            using var response = await http.GetAsync(E2ESettings.BaseUrl);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new XunitException(
                $"The frontend did not answer at {E2ESettings.BaseUrl}. Start the backend " +
                "(`docker compose up -d` in infra/) and the frontend (`npm run dev` in frontend/), " +
                $"or set E2E_BASE_URL to wherever it is running. ({exception.Message})");
        }
    }
}

/// <summary>A real, valid file for the upload step — the Design Service decides the type from the file's own bytes.</summary>
internal static class TestFiles
{
    // A 1x1 transparent PNG.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public static string WriteSamplePng(string run)
    {
        var path = Path.Combine(Path.GetTempPath(), $"e2e-floorplan-{run}.png");
        File.WriteAllBytes(path, Convert.FromBase64String(TinyPngBase64));

        return path;
    }
}
