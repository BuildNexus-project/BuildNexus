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
        browser.GoTo($"/projects/{projectId}/costs");

        browser.Type(By.Id("estimatedTotal"), estimatedTotal, "the estimated total field");
        browser.Click(By.XPath("//button[normalize-space()='Generate quotation']"), "the Generate quotation button");
        browser.UntilTrue(
            _ => browser.PageContains("Currently estimated at"),
            "the page to show the quotation as the current estimate");

        browser.Type(By.Id("amount"), invoiceAmount, "the invoice amount field");
        browser.Click(By.XPath("//button[normalize-space()='Raise invoice']"), "the Raise invoice button");
        browser.WaitForVisible(
            By.XPath("//table//*[normalize-space()='Pending']"),
            "the raised invoice listed as Pending");
    }

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
        browser.Click(trigger, what);
        browser.Click(
            By.XPath($"//*[@role='option'][normalize-space()='{optionText}']"),
            $"the '{optionText}' option in {what}");
    }
}
