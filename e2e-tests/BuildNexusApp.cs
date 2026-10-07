using OpenQA.Selenium;

namespace BuildNexus.EndToEnd.Tests;

/// <summary>An account created through the registration form.</summary>
public sealed record Account(string FullName, string Email, string Password, string Role);

/// <summary>
/// What a person can do in the BuildNexus UI, one method per thing they would do, so the test
/// reads as the workflow rather than as a list of selectors.
/// </summary>
/// <remarks>
/// Every method drives the real page — typing into inputs, clicking buttons — and returns only
/// once the screen shows that the action took effect. None calls an API directly: that is the
/// point of an end-to-end test, and the reason it can see what unit and integration tests cannot.
/// </remarks>
public sealed class BuildNexusApp(BrowserSession browser)
{
    /// <summary>Meets the password rules (8+ characters, a letter and a digit) the form enforces.</summary>
    public const string DefaultPassword = "E2ePass123!";

    /// <summary>Registers an account through <c>/register</c> and waits for the confirmation.</summary>
    public Account Register(string fullName, string email, string role, string roleLabel)
    {
        browser.GoTo("/register");

        browser.Type(By.Id("fullName"), fullName, "the full name field");
        browser.Type(By.Id("email"), email, "the email field");
        browser.Type(By.Id("password"), DefaultPassword, "the password field");
        ChooseFromSelect(By.Id("role"), roleLabel, "the role picker");
        browser.Click(By.CssSelector("form button[type='submit']"), "the Create account button");

        browser.WaitForVisible(
            By.XPath("//*[normalize-space()='Account created']"),
            $"the 'Account created' confirmation for {email}");

        return new Account(fullName, email, DefaultPassword, role);
    }

    /// <summary>Signs in through <c>/login</c> and waits until the signed-in header is showing.</summary>
    public void SignIn(string email, string password)
    {
        browser.GoTo("/login");

        browser.Type(By.Id("email"), email, "the email field");
        browser.Type(By.Id("password"), password, "the password field");
        browser.Click(By.CssSelector("form button[type='submit']"), "the Sign in button");

        // The header (and its Sign out button) only renders once a session exists, so it is the
        // proof the login worked — a wrong password would leave the form and an alert here.
        browser.WaitForVisible(SignOutButton, $"the signed-in header after signing in as {email}");
    }

    public void SignIn(Account account) => SignIn(account.Email, account.Password);

    /// <summary>Signs out from the header and waits for the signed-out landing page.</summary>
    public void SignOut()
    {
        browser.Click(SignOutButton, "the header's Sign out button");

        // The header disappears with the session; waiting for it to go is the proof.
        browser.UntilTrue(
            driver => driver.FindElements(SignOutButton).Count == 0,
            "the signed-in header to disappear after signing out");
    }

    /// <summary>
    /// Submits a project as the signed-in Client and returns the new project's id, read from the
    /// URL of the project page the confirmation links to.
    /// </summary>
    public Guid CreateProject(string name, string location)
    {
        browser.GoTo("/projects/new");

        browser.Type(By.Id("name"), name, "the project name field");
        browser.Type(By.Id("location"), location, "the location field");
        browser.Type(By.Id("landSizePerches"), "12", "the land size field");
        browser.Type(By.Id("budget"), "25000000", "the budget field");
        browser.Type(By.Id("floors"), "2", "the floors field");
        browser.Type(By.Id("bedrooms"), "3", "the bedrooms field");
        browser.Type(By.Id("bathrooms"), "2", "the bathrooms field");
        browser.Type(By.Id("garageSpaces"), "1", "the garage spaces field");
        browser.Click(By.CssSelector("form button[type='submit']"), "the Submit project button");

        browser.WaitForVisible(By.XPath("//*[normalize-space()='Project submitted']"), "the 'Project submitted' confirmation");
        browser.Click(By.XPath("//a[normalize-space()='View this project']"), "the View this project link");

        var projectUrl = browser.Until(
            driver => Guid.TryParse(driver.Url.Split('/').Last(), out _) ? driver.Url : null,
            "the browser to land on the new project's page");

        return Guid.Parse(projectUrl.Split('/').Last());
    }

    /// <summary>
    /// As the signed-in Admin, assigns the Architect and Project Manager to a project, and waits for
    /// each name to appear in the project's Team section.
    /// </summary>
    public void AssignStaff(Guid projectId, Account architect, Account projectManager)
    {
        browser.GoTo($"/projects/{projectId}");

        Assign("Assign architect", architect.FullName);
        browser.WaitForVisible(TeamMember("Architect", architect.FullName), $"{architect.FullName} listed as the project's Architect");

        Assign("Assign project manager", projectManager.FullName);
        browser.WaitForVisible(TeamMember("Project manager", projectManager.FullName), $"{projectManager.FullName} listed as the project's Project manager");
    }

    /// <summary>
    /// As the signed-in Architect, uploads a file as a new design document and waits for it to show
    /// up in the project's document list.
    /// </summary>
    public void UploadDesign(Guid projectId, string documentName, string filePath)
    {
        browser.GoTo($"/projects/{projectId}/designs");

        browser.Type(By.Id("name"), documentName, "the document name field");
        browser.Type(By.Id("revisionComment"), "First issue for client review.", "the revision comment field");
        // A file input takes the path as keys; no dialog opens.
        browser.WaitForVisible(By.Id("file"), "the file input").SendKeys(filePath);
        browser.Click(By.XPath("//form//button[normalize-space()='Upload']"), "the Upload button");

        browser.WaitForVisible(DesignVersionRow(documentName, "Submitted"), $"{documentName} listed with status Submitted");
    }

    /// <summary>As the signed-in Client, approves the design's first version and waits for it to read Approved.</summary>
    public void ApproveDesign(Guid projectId, string documentName)
    {
        browser.GoTo($"/projects/{projectId}/designs");

        browser.Click(
            By.XPath($"//section[.//h2[normalize-space()='{documentName}']]//button[normalize-space()='Approve']"),
            $"the Approve button on {documentName}");

        browser.WaitForVisible(DesignVersionRow(documentName, "Approved"), $"{documentName} listed with status Approved");
    }

    /// <summary>
    /// As the signed-in Project Manager, quotes the project and raises an invoice, waiting for each
    /// to appear in the page's own history.
    /// </summary>
    public void QuoteAndInvoice(Guid projectId, string estimatedTotal, string invoiceAmount)
    {
        Quote(projectId, estimatedTotal);

        browser.Type(By.Id("amount"), invoiceAmount, "the invoice amount field");
        browser.Click(By.XPath("//button[normalize-space()='Raise invoice']"), "the Raise invoice button");
        browser.WaitForVisible(
            By.XPath("//table//*[normalize-space()='Pending']"),
            "the raised invoice listed as Pending");
    }

    /// <summary>
    /// As the signed-in Project Manager, generates the project's quotation and waits for it to show
    /// as the current estimate. Starting construction later bills the quoted total automatically.
    /// </summary>
    public void Quote(Guid projectId, string estimatedTotal)
    {
        browser.GoTo($"/projects/{projectId}/costs");

        browser.Type(By.Id("estimatedTotal"), estimatedTotal, "the estimated total field");
        browser.Click(By.XPath("//button[normalize-space()='Generate quotation']"), "the Generate quotation button");
        browser.UntilTrue(
            _ => browser.PageContains("Currently estimated at"),
            "the page to show the quotation as the current estimate");
    }

    public void OpenProject(Guid projectId) => browser.GoTo($"/projects/{projectId}");

    /// <summary>
    /// As the signed-in Client, pays an invoice in full from <c>/my-costs</c> and returns once the
    /// invoice reads Paid and the outstanding balance reads zero.
    /// </summary>
    public void PayInvoice(string projectName, string amount)
    {
        browser.GoTo("/my-costs");

        // The invoice reaches the Client's page when the PM raises it, and the page reads it on
        // load, so waiting for the pay form is waiting for the real data.
        browser.Type(By.XPath(ProjectCard(projectName) + "//input[starts-with(@id,'amount-')]"), amount, "the payment amount field on the project's cost card");
        browser.Click(By.XPath(ProjectCard(projectName) + "//button[normalize-space()='Pay']"), "the Pay button");

        browser.WaitForVisible(By.XPath(ProjectCard(projectName) + "//*[normalize-space()='Paid']"), "the invoice badge to read Paid");
        // Intl's currency format puts a non-breaking space after "LKR", which XPath's
        // normalize-space() does not treat as whitespace — so it is translated to a plain one.
        browser.WaitForVisible(
            By.XPath($"//*[@aria-label='Outstanding balance for {projectName}'][normalize-space(translate(., ' ', ' '))='LKR 0.00']"),
            "the outstanding balance to show LKR 0.00");
    }

    /// <summary>
    /// Opens the signed-in user's notifications page and waits for one containing
    /// <paramref name="text"/>. These are created from Kafka events, so this is the visible proof
    /// an event crossed from one service to another.
    /// </summary>
    public void WaitForNotification(string text)
    {
        browser.GoTo("/notifications");

        // The page reads once on load and does not poll, so it is reloaded until the event's
        // notification exists rather than waiting on a screen that cannot change.
        browser.ReloadUntilVisible(
            By.XPath($"//*[contains(normalize-space(text()), '{text}')]"),
            $"a notification containing: {text}");
    }

    /// <summary>The milestones the standard construction template creates, in the order it creates them.</summary>
    public static readonly string[] TemplateMilestones =
        ["Foundation", "Walls", "Roof", "Electrical", "Plumbing", "Painting", "Finishing"];

    /// <summary>
    /// As the signed-in Architect, Project Manager or Admin, moves the project to the next status
    /// (for example "Design Approved") from the project page and waits for the status badge to change.
    /// </summary>
    public void MoveProjectTo(Guid projectId, string statusLabel)
    {
        browser.GoTo($"/projects/{projectId}");

        browser.Click(By.XPath($"//button[normalize-space()='Move to {statusLabel}']"), $"the 'Move to {statusLabel}' button");
        browser.WaitForVisible(StatusBadge(statusLabel), $"the project's status badge to read {statusLabel}");
    }

    /// <summary>
    /// As the signed-in Project Manager, creates the standard milestones. The Construction Service
    /// only learns the design is approved from a Kafka event, until when the page offers "Try
    /// again" instead, so the page is reloaded until the button appears.
    /// </summary>
    public void CreateMilestonesFromTemplate(Guid projectId)
    {
        browser.GoTo($"/projects/{projectId}");

        browser.ReloadUntilVisible(
            By.XPath("//button[normalize-space()='Create from template']"),
            "the Create from template button (the Construction Service must have heard the design was approved)");
        browser.Click(By.XPath("//button[normalize-space()='Create from template']"), "the Create from template button");

        browser.WaitForVisible(MilestoneRow(TemplateMilestones[0]), "the template milestones to be listed");
    }

    /// <summary>As the signed-in Project Manager, starts construction and waits for the build phase to read In Construction and the project to read Construction.</summary>
    public void StartConstruction()
    {
        browser.Click(By.XPath("//button[normalize-space()='Start construction']"), "the Start construction button");

        browser.WaitForVisible(PhaseBadge("In Construction"), "the build phase to read In Construction");
        browser.ReloadUntilVisible(StatusBadge("Construction"), "the project's status badge to read Construction");
    }

    /// <summary>As the signed-in Project Manager, sets every template milestone to Completed and waits for the progress to reach 100%.</summary>
    public void CompleteAllMilestones()
    {
        foreach (var milestone in TemplateMilestones)
        {
            ChooseFromSelect(
                By.CssSelector($"[aria-label='Change status of {milestone}']"),
                "Completed",
                $"the status picker for {milestone}");

            browser.WaitForVisible(
                By.XPath($"//tr[.//*[normalize-space()='{milestone}']]//*[@aria-label='Change status of {milestone}'][contains(normalize-space(), 'Completed')]"),
                $"{milestone} to read Completed");
        }

        browser.WaitForVisible(
            By.XPath($"//*[normalize-space()='{TemplateMilestones.Length} of {TemplateMilestones.Length} completed']"),
            "the milestone count to read all completed");
    }

    /// <summary>As the signed-in Project Manager, marks construction complete and waits for the build phase to say so.</summary>
    public void CompleteConstruction()
    {
        browser.Click(By.XPath("//button[normalize-space()='Mark construction complete']"), "the Mark construction complete button");

        browser.WaitForVisible(PhaseBadge("Construction Complete"), "the build phase to read Construction Complete");
    }

    /// <summary>
    /// As the signed-in Project Manager, hands the project over to the client. Handover is refused
    /// until the Payment Service has announced the final payment settled, so it is retried until
    /// the page confirms, and the project's status then reads Completed.
    /// </summary>
    public void HandOver(Guid projectId)
    {
        browser.RetryUntilVisible(
            () =>
            {
                browser.GoTo($"/projects/{projectId}");
                browser.Click(By.XPath("//button[normalize-space()='Hand over to client']"), "the Hand over to client button");
            },
            PhaseBadge("Handed Over"),
            "the build phase to read Handed Over (the final payment must have been announced as settled)");

        browser.ReloadUntilVisible(StatusBadge("Completed"), "the project's status badge to read Completed");
    }

    private static By StatusBadge(string label) =>
        By.XPath($"//*[@data-slot='card-title']//*[normalize-space()='{label}']");

    private static By PhaseBadge(string label) =>
        By.XPath($"//section[@data-testid='construction-phase-panel']//*[normalize-space()='{label}']");

    private static By MilestoneRow(string name) =>
        By.XPath($"//section[@data-testid='milestones-panel']//tr[.//*[normalize-space()='{name}']]");

    private static By SignOutButton => By.XPath("//header//button[normalize-space()='Sign out']");

    private static string ProjectCard(string projectName) =>
        $"//*[@data-slot='card'][.//*[normalize-space()='{projectName}']]";

    /// <summary>The value shown under a label in the project page's Team section.</summary>
    private static By TeamMember(string label, string name) =>
        By.XPath($"//section[.//h2[normalize-space()='Team']]//span[normalize-space(text())='{label}']/following-sibling::span[1][contains(normalize-space(), '{name}')]");

    private static By DesignVersionRow(string documentName, string status) =>
        By.XPath($"//section[.//h2[normalize-space()='{documentName}']]//tr[.//*[normalize-space()='{status}']]");

    /// <summary>Picks a person in one of the Admin's assignment dropdowns and presses its Assign button.</summary>
    private void Assign(string label, string personName)
    {
        ChooseFromSelect(By.CssSelector($"[id='{label}']"), personName, $"the '{label}' dropdown");
        browser.Click(By.CssSelector($"button[aria-label='{label}']"), $"the '{label}' button");
    }

    /// <summary>Opens a shadcn Select and clicks the option with the given text (its list renders in a portal).</summary>
    private void ChooseFromSelect(By trigger, string optionText, string what)
    {
        // A list that was just chosen from is still unmounting for a moment, and a click on the next
        // trigger in that moment is swallowed by it. Wait for it to be gone.
        browser.UntilTrue(
            driver => !driver.FindElements(By.XPath("//*[@role='option']")).Any(option => option.Displayed),
            "any previously opened dropdown list to close");

        browser.Click(trigger, what);

        // The page can hold several lists at once — the ones already used stay in the DOM, hidden —
        // so the option clicked is the visible one, not simply the first with that text.
        var option = browser.Until(
            driver => driver
                .FindElements(By.XPath($"//*[@role='option'][normalize-space()='{optionText}']"))
                .FirstOrDefault(candidate => candidate.Displayed && candidate.Enabled),
            $"the '{optionText}' option in {what}");
        option.Click();
    }
}
